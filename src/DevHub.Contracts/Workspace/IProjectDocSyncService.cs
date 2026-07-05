namespace DevHub.Contracts.Workspace;

/// <summary>
/// Cross-module seam that lets DevHub.Modules.WorkItems trigger doc sync operations
/// owned by DevHub.Modules.Workspace, without a direct project reference.
/// </summary>
public interface IProjectDocSyncService
{
    /// <summary>
    /// Assembles Markdown for every doc key and pushes files to the project's GitHub repo.
    /// Returns <c>null</c> when skipped (no repo/branch), <c>true</c> on full success,
    /// <c>false</c> when at least one file push failed.
    /// Never throws.
    /// </summary>
    Task<bool?> PushAllDocsToRepoAsync(Guid projectId, CancellationToken ct);

    /// <summary>
    /// Reads each doc file from the project's GitHub repo, parses the Markdown sections,
    /// and updates the corresponding <c>ProjectDocSection</c> records in the DB.
    /// Returns <c>null</c> when skipped (no repo/branch), <c>true</c> on full success,
    /// <c>false</c> when at least one file could not be pulled or parsed.
    /// Never throws.
    /// </summary>
    Task<bool?> PullDocsFromRepoAsync(Guid projectId, CancellationToken ct);
}
