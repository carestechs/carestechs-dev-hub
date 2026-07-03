using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Workspace.Entities;

public sealed class ProjectDoc : BaseEntity
{
    public required Guid ProjectId { get; set; }
    public required string DocKey { get; set; }
    public string? Content { get; set; }
    public Guid? UpdatedById { get; set; }
}
