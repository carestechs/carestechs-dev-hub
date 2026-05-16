using System.Text.Json;
using DevHub.Contracts.Audit;

namespace DevHub.Modules.Audit.DTOs;

public sealed record AuditActorDto(Guid Id, string DisplayName);

public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    AuditActorDto? ActingMember,
    Guid? ProjectId,
    string TargetType,
    Guid? TargetId,
    string Action,
    AuditOutcome Outcome,
    string? Reason,
    JsonElement? Details);
