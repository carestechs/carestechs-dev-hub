using System.ComponentModel.DataAnnotations;

namespace DevHub.Modules.Workspace.DTOs;

public sealed record DocTemplateVersionDto(
    Guid Id,
    int VersionNumber,
    bool IsActive,
    string? Notes,
    int SectionCount,
    int ProjectCount,
    DateTimeOffset CreatedAt);

public sealed class CreateDocTemplateVersionRequest
{
    [Required]
    public Guid SourceVersionId { get; init; }
    public string? Notes { get; init; }
}
