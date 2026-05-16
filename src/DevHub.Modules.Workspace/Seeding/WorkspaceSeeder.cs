using DevHub.Modules.Workspace.Entities;
using DevHub.Contracts.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevHub.Modules.Workspace.Seeding;

/// <summary>
/// Hosted service. On startup: applies the Workspace migrations, then idempotently
/// inserts the system <c>operator</c> role and the seed operator <see cref="Member"/>.
/// Identity creds for that member are seeded later by <c>IdentitySeeder</c>.
/// </summary>
public sealed class WorkspaceSeeder(
    IServiceProvider services,
    IOptions<WorkspaceSeedOptions> seed,
    ILogger<WorkspaceSeeder> logger) : IHostedService
{
    public const string OperatorRoleKey = "operator";

    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();

        await db.Database.MigrateAsync(ct);

        // 1. Operator role.
        var operatorRole = await db.Roles
            .FirstOrDefaultAsync(r => r.Key == OperatorRoleKey, ct);
        if (operatorRole is null)
        {
            operatorRole = new Role
            {
                Key = OperatorRoleKey,
                Name = "Operator",
                Description = "Workspace administrator. Manages teams, members, projects, and executors.",
                IsSystem = true,
            };
            db.Roles.Add(operatorRole);
            logger.LogInformation("Seeding system role {Key}", OperatorRoleKey);
        }

        // 2. Seed operator member.
        var seededMember = await db.Members
            .FirstOrDefaultAsync(m => m.Email == seed.Value.Email, ct);
        if (seededMember is null)
        {
            seededMember = new Member
            {
                DisplayName = seed.Value.DisplayName,
                Email = seed.Value.Email,
                Status = MemberStatus.Active,
            };
            db.Members.Add(seededMember);
            logger.LogInformation("Seeding operator member {Email}", seed.Value.Email);
        }

        await db.SaveChangesAsync(ct);

        // 3. Workspace-level operator role assignment for the seed member. We need the
        //    operator role id and the seed member id to exist; the SaveChanges above
        //    guarantees both have non-empty Ids (client-generated GUIDs from BaseEntity).
        var hasAssignment = await db.WorkspaceRoleAssignments
            .AnyAsync(w => w.MemberId == seededMember.Id && w.RoleId == operatorRole.Id, ct);
        if (!hasAssignment)
        {
            db.WorkspaceRoleAssignments.Add(new WorkspaceRoleAssignment
            {
                MemberId = seededMember.Id,
                RoleId = operatorRole.Id,
                CreatedByMemberId = seededMember.Id, // self-grant; bootstrap
            });
            logger.LogInformation("Granting workspace-level operator role to seed member {Email}", seed.Value.Email);
            await db.SaveChangesAsync(ct);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Read by <see cref="WorkspaceSeeder"/>. Mirrors <c>OperatorSeedOptions</c> from
/// the API host but uses its own type so the module doesn't reference Api options.
/// </summary>
public sealed class WorkspaceSeedOptions
{
    public const string SectionName = "OperatorSeed";

    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}
