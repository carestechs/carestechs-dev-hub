namespace DevHub.Contracts.WorkItems;

/// <summary>
/// Cross-module read-side view of work items. Published by the WorkItems module so other modules
/// (Notifications, Audit query layer) don't need a project reference to the module.
/// </summary>
public interface IWorkItemLookup
{
    Task<WorkItemLookupResult?> FindByIdAsync(Guid workItemId, CancellationToken cancellationToken = default);

    /// All work item ids in a project that are currently <c>WaitingOnCheckpoint</c>.
    /// Used by the notifications reconciler when a member is added to a project to backfill rows.
    Task<IReadOnlyList<Guid>> ListWaitingForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public sealed record WorkItemLookupResult(
    Guid Id,
    Guid ProjectId,
    Guid ExecutorId,
    string CurrentStatus,
    string? CurrentCheckpointKey,
    string Title);
