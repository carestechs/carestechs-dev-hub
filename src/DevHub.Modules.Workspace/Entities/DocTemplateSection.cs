using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Workspace.Entities;

public sealed class DocTemplateSection : BaseEntity
{
    public required Guid VersionId { get; set; }
    public required string DocKey { get; set; }
    public required string SectionKey { get; set; }
    public required string Label { get; set; }
    public string? Hint { get; set; }
    public required bool Required { get; set; }
    public required int DisplayOrder { get; set; }
}
