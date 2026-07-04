using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Contracts.Executors;
using DevHub.Contracts.Pagination;
using DevHub.Contracts.Validation;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Entities;
using DevHub.Modules.Workspace.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevHub.Modules.Workspace.Services;

public interface IProjectService
{
    Task<PagedEnvelopeDto<ProjectDto>> ListAsync(PageRequest page, Guid? teamId, string? projectType, Guid callerMemberId, CancellationToken ct);
    Task<ProjectDto> GetAsync(Guid id, Guid callerMemberId, CancellationToken ct);
    Task<ProjectDto> GetBySlugAsync(string slug, Guid callerMemberId, CancellationToken ct);
    Task<ProjectDto> CreateAsync(CreateProjectRequest req, Guid actingMemberId, CancellationToken ct);
    Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest req, Guid actingMemberId, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid actingMemberId, CancellationToken ct);
}

internal sealed class ProjectService(
    WorkspaceDbContext db,
    IProjectAuthorizationService authz,
    IProjectMembershipQuery memberships,
    IExecutorRouter router,
    IAuditWriter audit,
    IGitHubService gitHub,
    ILogger<ProjectService> logger) : IProjectService
{
    public async Task<PagedEnvelopeDto<ProjectDto>> ListAsync(
        PageRequest page, Guid? teamId, string? projectType, Guid callerMemberId, CancellationToken ct)
    {
        IQueryable<Project> query = db.Projects.AsNoTracking();

        // Operators see all; others see only the projects they have a ProjectMembership on.
        if (!await memberships.IsOperatorAsync(callerMemberId, ct))
        {
            query = from p in query
                    join pm in db.ProjectMemberships on p.Id equals pm.ProjectId
                    where pm.MemberId == callerMemberId
                    select p;
        }
        if (teamId is { } t) query = query.Where(p => p.OwningTeamId == t);
        if (!string.IsNullOrWhiteSpace(projectType)) query = query.Where(p => p.ProjectType == projectType);

        var totalCount = await query.CountAsync(ct);
        query = (page.SortBy?.ToLowerInvariant(), page.SortDir) switch
        {
            ("name", "asc")    => query.OrderBy(p => p.Name),
            ("name", _)        => query.OrderByDescending(p => p.Name),
            ("updatedat", "asc") => query.OrderBy(p => p.UpdatedAt),
            ("updatedat", _)   => query.OrderByDescending(p => p.UpdatedAt),
            (_, "asc")         => query.OrderBy(p => p.CreatedAt),
            _                  => query.OrderByDescending(p => p.CreatedAt),
        };

        var rows = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.ProjectType, p.Description, p.Repo, p.DefaultBranch, p.CreatedAt, p.OwningTeamId,
                Team = db.Teams.Where(t => t.Id == p.OwningTeamId).Select(t => new { t.Id, t.Name }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        // IMP-001: BoundExecutorProtocol is intentionally null on list rows — list
        // doesn't open the Start Work modal, so the per-row IExecutorRouter call would
        // be an N+1 with no consumer.
        var dtos = rows.Select(r => new ProjectDto(
            r.Id, r.Name, r.Slug, r.ProjectType,
            new TeamRefDto(r.Team?.Id ?? r.OwningTeamId, r.Team?.Name ?? string.Empty),
            r.Description, r.Repo, r.DefaultBranch, InFlightWorkItems: 0, r.CreatedAt,
            BoundExecutorProtocol: null)).ToList();

        return new PagedEnvelopeDto<ProjectDto>(dtos, new PageMeta(totalCount, page.Page, page.PageSize, page.SortBy, page.SortDir));
    }

    public async Task<ProjectDto> GetAsync(Guid id, Guid callerMemberId, CancellationToken ct)
    {
        await authz.EnsureAuthorizedAsync(callerMemberId, id, "project:read", requiredRoleKey: null, ct);
        return await LoadAsync(p => p.Id == id, ct);
    }

    public async Task<ProjectDto> GetBySlugAsync(string slug, Guid callerMemberId, CancellationToken ct)
    {
        var projectId = await db.Projects.AsNoTracking()
            .Where(p => p.Slug == slug)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Project not found.");
        await authz.EnsureAuthorizedAsync(callerMemberId, projectId, "project:read", requiredRoleKey: null, ct);
        return await LoadAsync(p => p.Id == projectId, ct);
    }

    public async Task<ProjectDto> CreateAsync(CreateProjectRequest req, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "project:create", ct);

        // Boundary validation for the optional code-source coordinates — parity with the
        // upstream orchestrator's intake.codeSource schema (FEAT-008).
        if (req.Repo is not null) CodeSourceValidator.ValidateRepo(req.Repo, fieldName: "repo");
        if (req.DefaultBranch is not null) CodeSourceValidator.ValidateBranch(req.DefaultBranch, fieldName: "defaultBranch");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (!await db.Teams.AnyAsync(t => t.Id == req.OwningTeamId, ct))
            throw new ConflictException($"Owning team '{req.OwningTeamId}' does not exist.");
        if (await db.Projects.AnyAsync(p => p.Slug == req.Slug, ct))
            throw new ConflictException($"A project with slug '{req.Slug}' already exists.");
        if (await db.Projects.AnyAsync(p => p.Name == req.Name, ct))
            throw new ConflictException($"A project with name '{req.Name}' already exists.");

        if (!await router.IsProjectTypeBoundAsync(req.ProjectType, ct))
            throw new ConflictException($"No executor bound for project type '{req.ProjectType}'.");

        var activeVersionId = await db.DocTemplateVersions
            .AsNoTracking()
            .Where(v => v.IsActive)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new ConflictException("No active documentation template version configured. Contact your workspace operator.");

        var project = new Project
        {
            Name = req.Name,
            Slug = req.Slug,
            ProjectType = req.ProjectType,
            OwningTeamId = req.OwningTeamId,
            Description = req.Description,
            Repo = req.Repo,
            DefaultBranch = req.DefaultBranch,
            DocTemplateVersionId = activeVersionId,
        };
        db.Projects.Add(project);

        await audit.WriteAsync(new AuditWriteRequest("Project", project.Id, "project:create", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            ProjectId = project.Id,
            Details = new Dictionary<string, object?>
            {
                ["slug"] = req.Slug,
                ["projectType"] = req.ProjectType,
                ["repo"] = req.Repo,
                ["defaultBranch"] = req.DefaultBranch,
            },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Post-commit: attempt GitHub repo creation if requested.
        List<string>? warnings = null;
        if (req.CreateGitHubRepo)
        {
            var repoName = !string.IsNullOrWhiteSpace(req.RepoName)
                ? req.RepoName
                : SlugifyRepoName(req.Name);
            try
            {
                var fullName = await gitHub.CreateRepoAsync(repoName, ct);
                project.Repo = fullName;
                await db.SaveChangesAsync(ct);

                await audit.WriteAsync(new AuditWriteRequest("Project", project.Id, "project:github-repo-created", AuditOutcome.Granted)
                {
                    ActingMemberId = actingMemberId,
                    ProjectId = project.Id,
                    Details = new Dictionary<string, object?> { ["repo"] = fullName },
                }, ct);

                await gitHub.SeedScaffoldAsync(fullName, ct);
            }
            catch (GitHubApiException ex)
            {
                logger.LogWarning(ex, "GitHub repo creation failed for project {ProjectId} (repoName={RepoName})", project.Id, repoName);
                warnings = ["githubRepoCreationFailed"];
            }
        }

        var dto = await LoadAsync(p => p.Id == project.Id, ct);
        return warnings is { Count: > 0 } ? dto with { Warnings = warnings } : dto;
    }

    public async Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest req, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "project:update", ct);

        // Boundary validation runs before any DB write — denials never mutate state.
        if (req.Repo is not null) CodeSourceValidator.ValidateRepo(req.Repo, fieldName: "repo");
        if (req.DefaultBranch is not null) CodeSourceValidator.ValidateBranch(req.DefaultBranch, fieldName: "defaultBranch");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Project not found.");

        var repoBefore = project.Repo;
        var defaultBranchBefore = project.DefaultBranch;

        if (req.Name is not null && req.Name != project.Name)
        {
            if (await db.Projects.AnyAsync(p => p.Name == req.Name && p.Id != id, ct))
                throw new ConflictException($"A project with name '{req.Name}' already exists.");
            project.Name = req.Name;
        }
        if (req.Description is not null) project.Description = req.Description;
        if (req.ProjectType is not null) project.ProjectType = req.ProjectType;
        if (req.Repo is not null) project.Repo = req.Repo;
        if (req.DefaultBranch is not null) project.DefaultBranch = req.DefaultBranch;

        var details = new Dictionary<string, object?>();
        if (repoBefore != project.Repo)
        {
            details["repoBefore"] = repoBefore;
            details["repoAfter"] = project.Repo;
        }
        if (defaultBranchBefore != project.DefaultBranch)
        {
            details["defaultBranchBefore"] = defaultBranchBefore;
            details["defaultBranchAfter"] = project.DefaultBranch;
        }

        await audit.WriteAsync(new AuditWriteRequest("Project", project.Id, "project:update", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            ProjectId = project.Id,
            Details = details.Count > 0 ? details : null,
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await LoadAsync(p => p.Id == project.Id, ct);
    }

    public async Task DeleteAsync(Guid id, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "project:delete", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Project not found.");

        var now = DateTimeOffset.UtcNow;
        project.DeletedAt = now;

        // Soft-cascade memberships + role assignments.
        var membershipIds = await db.ProjectMemberships
            .Where(pm => pm.ProjectId == id)
            .Select(pm => pm.Id)
            .ToListAsync(ct);
        if (membershipIds.Count > 0)
        {
            await db.RoleAssignments
                .Where(ra => membershipIds.Contains(ra.ProjectMembershipId))
                .ExecuteUpdateAsync(s => s.SetProperty(ra => ra.DeletedAt, now), ct);
            await db.ProjectMemberships
                .Where(pm => pm.ProjectId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(pm => pm.DeletedAt, now), ct);
        }

        await audit.WriteAsync(new AuditWriteRequest("Project", project.Id, "project:delete", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            ProjectId = project.Id,
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    internal static string SlugifyRepoName(string projectName) =>
        System.Text.RegularExpressions.Regex
            .Replace(projectName.ToLowerInvariant(), @"[^a-z0-9]+", "-")
            .Trim('-');

    private async Task<ProjectDto> LoadAsync(System.Linq.Expressions.Expression<Func<Project, bool>> predicate, CancellationToken ct)
    {
        var row = await db.Projects.AsNoTracking()
            .Where(predicate)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.ProjectType, p.Description, p.Repo, p.DefaultBranch, p.CreatedAt, p.OwningTeamId,
                Team = db.Teams.Where(t => t.Id == p.OwningTeamId).Select(t => new { t.Id, t.Name }).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Project not found.");
        var descriptor = await router.ResolveAsync(row.Id, ct);
        return new ProjectDto(
            row.Id, row.Name, row.Slug, row.ProjectType,
            new TeamRefDto(row.Team?.Id ?? row.OwningTeamId, row.Team?.Name ?? string.Empty),
            row.Description, row.Repo, row.DefaultBranch, InFlightWorkItems: 0, row.CreatedAt,
            BoundExecutorProtocol: descriptor?.Protocol);
    }
}
