using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Workspace.Entities;

public sealed class RoleAssignment : BaseEntity, ISoftDeletable
{
    public required Guid ProjectMembershipId { get; set; }
    public required Guid RoleId { get; set; }
    public required Guid CreatedByMemberId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
