using DevHub.Contracts.Workspace;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Workspace.Services;

internal sealed class ProjectLookup(WorkspaceDbContext db) : IProjectLookup
{
    public async Task<string?> GetProjectTypeAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId && p.DeletedAt == null)
            .Select(p => p.ProjectType)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<ProjectLookupResult?> FindByIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var row = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId && p.DeletedAt == null)
            .Select(p => new { p.Id, p.Name, p.Slug, p.ProjectType, p.OwningTeamId, p.Repo, p.DefaultBranch })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new ProjectLookupResult(row.Id, row.Name, row.Slug, row.ProjectType, row.OwningTeamId, row.Repo, row.DefaultBranch);
    }
}
