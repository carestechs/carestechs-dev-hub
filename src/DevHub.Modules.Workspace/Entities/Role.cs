using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Workspace.Entities;

public sealed class Role : BaseEntity
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
}
