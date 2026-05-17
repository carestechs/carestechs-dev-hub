using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Workspace.Entities;

public sealed class Project : BaseEntity, ISoftDeletable
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public required string ProjectType { get; set; }
    public required Guid OwningTeamId { get; set; }
    public string? Description { get; set; }
    public string? Repo { get; set; }
    public string? DefaultBranch { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
