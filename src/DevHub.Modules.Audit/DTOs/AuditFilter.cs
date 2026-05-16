using DevHub.Contracts.Audit;

namespace DevHub.Modules.Audit.DTOs;

/// <summary>
/// Optional filters applied by <see cref="Services.IAuditQueryService"/>. All fields are
/// AND-combined; null/empty fields are no-ops. <see cref="ProjectId"/> is ignored by the
/// project-scoped query (which already constrains to one project).
/// </summary>
public sealed record AuditFilter
{
    public Guid? ActingMemberId { get; init; }
    public string? TargetType { get; init; }
    public string? Action { get; init; }
    public AuditOutcome? Outcome { get; init; }
    public Guid? ProjectId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}
