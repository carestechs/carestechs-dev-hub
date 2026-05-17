using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Notifications.Entities;

/// <summary>
/// Live "waiting on you" entry for a specific member-checkpoint pair. Recomputed on every WorkItem
/// transition; <see cref="DismissedAt"/> is briefly kept after resolution so the UI can fade the
/// row out, then a FEAT-006 archive task hard-purges old rows.
/// </summary>
public sealed class PendingActionSignal : BaseEntity
{
    public required Guid MemberId { get; set; }
    public required Guid ProjectId { get; set; }
    public required Guid WorkItemId { get; set; }
    public required string CheckpointKey { get; set; }
    public string? TaskId { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
}
