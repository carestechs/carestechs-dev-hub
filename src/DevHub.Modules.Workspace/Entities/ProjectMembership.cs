using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Workspace.Entities;

public sealed class ProjectMembership : BaseEntity, ISoftDeletable
{
    public required Guid ProjectId { get; set; }
    public required Guid MemberId { get; set; }
    public required Guid CreatedByMemberId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
