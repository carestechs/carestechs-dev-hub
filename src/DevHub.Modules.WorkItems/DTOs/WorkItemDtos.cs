using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace DevHub.Modules.WorkItems.DTOs;

public sealed record ExecutorRefDto(Guid Id, string Key, string DisplayName);
public sealed record MemberRefDto(Guid Id, string DisplayName);

public sealed record WorkItemSummaryDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string CurrentStatus,
    string? CurrentCheckpointKey,
    ExecutorRefDto Executor,
    string ExecutorCorrelationMarker,
    DateTimeOffset CreatedAt,
    MemberRefDto CreatedBy,
    string? WorkBranch,
    string? CurrentTaskId = null,
    Guid? ExecutorRunId = null,
    string? PrUrl = null);

public sealed record WorkItemDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string CurrentStatus,
    string? CurrentCheckpointKey,
    ExecutorRefDto Executor,
    string ExecutorCorrelationMarker,
    DateTimeOffset CreatedAt,
    MemberRefDto CreatedBy,
    JsonElement ExecutorState,
    string? WorkBranch,
    string? CurrentTaskId = null,
    Guid? ExecutorRunId = null,
    string? PrUrl = null);

public sealed record CheckpointSignalDto(
    Guid Id,
    string CheckpointKey,
    string Outcome,
    MemberRefDto SignaledBy,
    DateTimeOffset SignaledAt,
    int? ExecutorResponseStatus,
    JsonElement? Payload);

public sealed class StartWorkItemRequest
{
    [Required, MaxLength(255)]
    public string Title { get; init; } = string.Empty;

    public JsonElement Input { get; init; }

    [MaxLength(200)]
    public string? WorkBranch { get; init; }
}

public sealed record UpdateWorkItemRequest
{
    /// <summary>
    /// null = leave unchanged. Empty string ("") = clear the override (fall back
    /// to the project's default branch). Any other non-null value is validated
    /// against the branch-shorthand rules and persisted.
    /// </summary>
    [MaxLength(200)]
    public string? WorkBranch { get; init; }
}

public sealed class SignalRequest
{
    [Required, MaxLength(60)]
    public string Outcome { get; init; } = string.Empty;

    public JsonElement? Payload { get; init; }

    /// <summary>
    /// Identifier of the task this signal targets (FEAT-009). Required by the executor when
    /// the active contract is per-task; DevHub forwards it verbatim. Omitted from the body
    /// when null — never sent as <c>null</c>, matching the orchestrator's omit-don't-null
    /// pattern.
    /// </summary>
    [MaxLength(60)]
    public string? TaskId { get; init; }
}
