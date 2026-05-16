namespace DevHub.Contracts.Notifications;

/// <summary>
/// One emitted-by-reconciler event delivered to each subscribed member's SSE stream.
/// </summary>
public sealed record PendingActionEvent(
    string Kind, // "raised" | "dismissed"
    Guid MemberId,
    Guid ProjectId,
    Guid WorkItemId,
    string CheckpointKey,
    DateTimeOffset OccurredAt);
