namespace DevHub.Contracts.Notifications;

/// <summary>
/// Called by WorkItems / Workspace on every state transition that could change who is waiting on
/// what. Synchronous, transactional, idempotent — repeated calls with no state change write no
/// rows. The implementation upserts <c>PendingActionSignal</c> rows and publishes
/// <see cref="PendingActionEvent"/>s to the in-process stream registry.
/// </summary>
public interface IPendingActionReconciler
{
    Task RecomputeForWorkItemAsync(Guid workItemId, CancellationToken cancellationToken = default);

    Task RecomputeForMemberInProjectAsync(Guid memberId, Guid projectId, CancellationToken cancellationToken = default);
}
