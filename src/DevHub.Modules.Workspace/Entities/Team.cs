using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Workspace.Entities;

public sealed class Team : BaseEntity, ISoftDeletable
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
