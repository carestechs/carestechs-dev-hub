using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Workspace.Entities;

public sealed class ProjectDocSection : BaseEntity
{
    public required Guid ProjectId { get; set; }
    public required Guid SectionId { get; set; }
    public string? Content { get; set; }
    public Guid? UpdatedById { get; set; }
}
