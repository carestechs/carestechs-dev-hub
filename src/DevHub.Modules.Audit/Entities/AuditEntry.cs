using DevHub.Contracts.Audit;
using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Audit.Entities;

public sealed class AuditEntry : BaseEntity
{
    public required DateTimeOffset OccurredAt { get; set; }
    public Guid? ActingMemberId { get; set; }
    public Guid? ProjectId { get; set; }
    public required string TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public required string Action { get; set; }
    public required AuditOutcome Outcome { get; set; }
    public string? Reason { get; set; }
    public string? DetailsJson { get; set; }
}
