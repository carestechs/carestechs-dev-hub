using System.ComponentModel.DataAnnotations;

namespace DevHub.Modules.Workspace.DTOs;

public sealed record ProjectDocSummaryDto(
    string Key,
    string Label,
    string Description,
    bool Filled,
    DateTimeOffset? FilledAt,
    string? UpdatedByName);

public sealed record ProjectDocDto(
    string Key,
    string Label,
    string Description,
    string Hints,
    string? Content,
    bool Filled,
    DateTimeOffset? FilledAt,
    string? UpdatedByName);

public sealed class UpsertDocRequest
{
    [Required]
    public string Content { get; init; } = string.Empty;
}
