using System.ComponentModel.DataAnnotations;

namespace DevHub.Modules.Workspace.DTOs;

public sealed record ProjectDocSectionSummaryDto(
    string Key,
    string Label,
    bool Required,
    bool Filled);

public sealed record ProjectDocSummaryDto(
    string DocKey,
    string Label,
    string Description,
    bool Filled,
    bool Locked,
    int FilledSectionCount,
    int TotalSectionCount,
    IReadOnlyList<ProjectDocSectionSummaryDto> Sections);

public sealed record ProjectDocSectionDto(
    string Key,
    string Label,
    string? Hint,
    bool Required,
    string? Content,
    bool Filled,
    DateTimeOffset? FilledAt,
    string? UpdatedByName);

public sealed record ProjectDocDto(
    string DocKey,
    string Label,
    string Description,
    bool Filled,
    bool Locked,
    IReadOnlyList<ProjectDocSectionDto> Sections,
    bool? RepoSynced = null);

public sealed class UpsertDocSectionsRequest
{
    [Required]
    public required Dictionary<string, string> Sections { get; init; }
}
