using DevHub.Contracts.Persistence;

namespace DevHub.Modules.WorkItems.Entities;

// Never soft-deleted: cancellation is a status transition (data-model.md §247).
public sealed class WorkItem : BaseEntity
{
    public required Guid ProjectId { get; set; }
    public required Guid ExecutorId { get; set; }
    public required string ExecutorCorrelationMarker { get; set; }
    public required string Title { get; set; }
    public required string CurrentStatus { get; set; }
    public string? CurrentCheckpointKey { get; set; }
    public required Guid CreatedByMemberId { get; set; }
    public string? WorkBranch { get; set; }
}
