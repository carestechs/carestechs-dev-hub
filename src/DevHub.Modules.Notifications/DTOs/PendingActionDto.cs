namespace DevHub.Modules.Notifications.DTOs;

/// Mirrors docs/api-spec.md §Notifications · GET /api/notifications/pending response.
public sealed record PendingActionDto(
    Guid ProjectId,
    string ProjectSlug,
    Guid WorkItemId,
    string WorkItemTitle,
    string CheckpointKey,
    string CheckpointDisplayName,
    DateTimeOffset RaisedAt,
    string? TaskId = null);
