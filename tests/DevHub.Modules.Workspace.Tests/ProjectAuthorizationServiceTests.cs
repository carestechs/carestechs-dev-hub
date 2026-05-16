using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Modules.Audit;
using DevHub.Modules.Audit.Entities;
using DevHub.Modules.Audit.Services;
using DevHub.Modules.Workspace.Entities;
using DevHub.Modules.Workspace.Services;
using DevHub.TestHarness;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevHub.Modules.Workspace.Tests;

[Collection("postgres")]
public class ProjectAuthorizationServiceTests(PostgresFixture pg)
{
    private const string OperatorRoleKey = "operator";
    private const string ReviewerRoleKey = "reviewer";

    /// <summary>
    /// Spin up a fresh isolated Postgres DB, apply BOTH modules' migrations on it, and return
    /// the sut alongside the contexts so tests can seed + assert directly.
    /// </summary>
    private async Task<Fixture> NewAsync()
    {
        var connStr = await pg.CreateIsolatedDatabaseAsync($"authz_{Guid.NewGuid():N}");

        var wsOpts = new DbContextOptionsBuilder<WorkspaceDbContext>()
            .UseNpgsql(connStr).UseSnakeCaseNamingConvention().Options;
        var ws = new WorkspaceDbContext(wsOpts);
        await ws.Database.MigrateAsync();

        var auditOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(connStr).UseSnakeCaseNamingConvention().Options;
        var audit = new AuditDbContext(auditOpts);
        await audit.Database.MigrateAsync();

        var writer = new AuditWriter(audit);
        var query = new ProjectMembershipQuery(ws);
        var sut = new ProjectAuthorizationService(ws, query, writer);

        return new Fixture(ws, audit, sut, connStr);
    }

    private sealed record Fixture(WorkspaceDbContext Workspace, AuditDbContext Audit, ProjectAuthorizationService Sut, string ConnectionString)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Workspace.DisposeAsync();
            await Audit.DisposeAsync();
        }
    }

    private static async Task<(Role op, Role reviewer)> SeedRolesAsync(WorkspaceDbContext ws)
    {
        var op = new Role { Key = OperatorRoleKey, Name = "Operator", IsSystem = true };
        var rev = new Role { Key = ReviewerRoleKey, Name = "Reviewer" };
        ws.Roles.AddRange(op, rev);
        await ws.SaveChangesAsync();
        return (op, rev);
    }

    private static async Task<Team> SeedTeamAsync(WorkspaceDbContext ws)
    {
        var team = new Team { Name = $"Eng_{Guid.NewGuid():N}" };
        ws.Teams.Add(team);
        await ws.SaveChangesAsync();
        return team;
    }

    private static async Task<Member> SeedMemberAsync(WorkspaceDbContext ws, string suffix = "")
    {
        var member = new Member
        {
            DisplayName = "Member " + suffix,
            Email = $"m{suffix}-{Guid.NewGuid():N}@test.local",
        };
        ws.Members.Add(member);
        await ws.SaveChangesAsync();
        return member;
    }

    private static async Task<Project> SeedProjectAsync(WorkspaceDbContext ws, Guid teamId)
    {
        var project = new Project
        {
            Name = $"Proj_{Guid.NewGuid():N}",
            Slug = $"proj-{Guid.NewGuid():N}".Substring(0, 20),
            ProjectType = "feature-delivery",
            OwningTeamId = teamId,
        };
        ws.Projects.Add(project);
        await ws.SaveChangesAsync();
        return project;
    }

    [Fact]
    public async Task Operator_is_granted_workspace_wide_with_audit()
    {
        await using var f = await NewAsync();
        var (op, _) = await SeedRolesAsync(f.Workspace);
        var team = await SeedTeamAsync(f.Workspace);
        var member = await SeedMemberAsync(f.Workspace);
        var project = await SeedProjectAsync(f.Workspace, team.Id);
        f.Workspace.WorkspaceRoleAssignments.Add(new WorkspaceRoleAssignment
        {
            MemberId = member.Id, RoleId = op.Id, CreatedByMemberId = member.Id,
        });
        await f.Workspace.SaveChangesAsync();

        var outcome = await f.Sut.AuthorizeAsync(member.Id, project.Id, "workitem:start");

        outcome.Granted.Should().BeTrue();
        var audit = await f.Audit.AuditEntries.SingleAsync();
        audit.Outcome.Should().Be(AuditOutcome.Granted);
        audit.Reason.Should().Be("operator");
    }

    [Fact]
    public async Task Non_member_is_denied_with_audit()
    {
        await using var f = await NewAsync();
        await SeedRolesAsync(f.Workspace);
        var team = await SeedTeamAsync(f.Workspace);
        var member = await SeedMemberAsync(f.Workspace);
        var project = await SeedProjectAsync(f.Workspace, team.Id);

        var outcome = await f.Sut.AuthorizeAsync(member.Id, project.Id, "workitem:start");

        outcome.Granted.Should().BeFalse();
        outcome.DeniedReason.Should().Contain("not on this project");
        var audit = await f.Audit.AuditEntries.SingleAsync();
        audit.Outcome.Should().Be(AuditOutcome.Denied);
    }

    [Fact]
    public async Task Member_with_required_role_is_granted_with_audit()
    {
        await using var f = await NewAsync();
        var (_, reviewer) = await SeedRolesAsync(f.Workspace);
        var team = await SeedTeamAsync(f.Workspace);
        var member = await SeedMemberAsync(f.Workspace);
        var project = await SeedProjectAsync(f.Workspace, team.Id);
        var membership = new ProjectMembership
        {
            ProjectId = project.Id, MemberId = member.Id, CreatedByMemberId = member.Id,
        };
        f.Workspace.ProjectMemberships.Add(membership);
        await f.Workspace.SaveChangesAsync();
        f.Workspace.RoleAssignments.Add(new RoleAssignment
        {
            ProjectMembershipId = membership.Id, RoleId = reviewer.Id, CreatedByMemberId = member.Id,
        });
        await f.Workspace.SaveChangesAsync();

        var outcome = await f.Sut.AuthorizeAsync(member.Id, project.Id, "workitem:review", requiredRoleKey: ReviewerRoleKey);

        outcome.Granted.Should().BeTrue();
        var audit = await f.Audit.AuditEntries.SingleAsync();
        audit.Outcome.Should().Be(AuditOutcome.Granted);
    }

    [Fact]
    public async Task Member_without_required_role_is_denied_with_audit()
    {
        await using var f = await NewAsync();
        await SeedRolesAsync(f.Workspace);
        var team = await SeedTeamAsync(f.Workspace);
        var member = await SeedMemberAsync(f.Workspace);
        var project = await SeedProjectAsync(f.Workspace, team.Id);
        var membership = new ProjectMembership
        {
            ProjectId = project.Id, MemberId = member.Id, CreatedByMemberId = member.Id,
        };
        f.Workspace.ProjectMemberships.Add(membership);
        await f.Workspace.SaveChangesAsync();
        // NOTE: no role assigned.

        var outcome = await f.Sut.AuthorizeAsync(member.Id, project.Id, "workitem:approve",
            requiredRoleKey: "approver");

        outcome.Granted.Should().BeFalse();
        outcome.DeniedReason.Should().Contain("lacks role 'approver'");
        var audit = await f.Audit.AuditEntries.SingleAsync();
        audit.Outcome.Should().Be(AuditOutcome.Denied);
    }

    [Fact]
    public async Task Member_with_no_required_role_specified_is_granted_for_reads()
    {
        await using var f = await NewAsync();
        await SeedRolesAsync(f.Workspace);
        var team = await SeedTeamAsync(f.Workspace);
        var member = await SeedMemberAsync(f.Workspace);
        var project = await SeedProjectAsync(f.Workspace, team.Id);
        f.Workspace.ProjectMemberships.Add(new ProjectMembership
        {
            ProjectId = project.Id, MemberId = member.Id, CreatedByMemberId = member.Id,
        });
        await f.Workspace.SaveChangesAsync();

        var outcome = await f.Sut.AuthorizeAsync(member.Id, project.Id, "workitem:read", requiredRoleKey: null);

        outcome.Granted.Should().BeTrue();
    }
}
