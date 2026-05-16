namespace DevHub.Contracts.Audit;

/// <summary>
/// Payload for <see cref="IAuditWriter.WriteAsync"/>. Records who did what,
/// to which target, with what outcome — captured before forwarding to any downstream
/// system so denied / failed actions are persisted exactly like granted ones.
/// </summary>
public sealed record AuditWriteRequest(
    string TargetType,
    Guid? TargetId,
    string Action,
    AuditOutcome Outcome)
{
    public Guid? ActingMemberId { get; init; }

    public Guid? ProjectId { get; init; }

    /// Short human-readable reason; surfaced in the operator dashboard. Required on Denied / Failed in practice.
    public string? Reason { get; init; }

    /// Override the timestamp. Defaults to <c>DateTimeOffset.UtcNow</c> at write time.
    public DateTimeOffset? OccurredAt { get; init; }

    /// Arbitrary structured details. Serialized to <c>jsonb</c>; keep small.
    public IReadOnlyDictionary<string, object?>? Details { get; init; }
}
