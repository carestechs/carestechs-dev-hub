using DevHub.Contracts.Persistence;

namespace DevHub.Modules.WorkItems.Entities;

public sealed class CheckpointSignal : BaseEntity
{
    public required Guid WorkItemId { get; set; }
    public required string CheckpointKey { get; set; }
    public required string Outcome { get; set; }
    public string? PayloadJson { get; set; }
    public required Guid SignaledByMemberId { get; set; }
    public required DateTimeOffset SignaledAt { get; set; }
    public int? ExecutorResponseStatus { get; set; }
    public DateTimeOffset? ExecutorResponseAt { get; set; }
    public string? IdempotencyKey { get; set; }
}
