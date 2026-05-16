namespace DevHub.Contracts.Workspace;

/// <summary>
/// Cross-module project lookup published by Workspace. Lets other modules read minimal project
/// facts (id, slug, projectType, owningTeamId) without depending on Workspace entities.
/// </summary>
public interface IProjectLookup
{
    Task<string?> GetProjectTypeAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectLookupResult?> FindByIdAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public sealed record ProjectLookupResult(Guid Id, string Name, string Slug, string ProjectType, Guid OwningTeamId);
