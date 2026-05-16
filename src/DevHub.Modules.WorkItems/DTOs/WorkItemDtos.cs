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
    MemberRefDto CreatedBy);

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
    JsonElement ExecutorState);

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
}

public sealed class SignalRequest
{
    [Required, MaxLength(60)]
    public string Outcome { get; init; } = string.Empty;

    public JsonElement? Payload { get; init; }
}
