using DevHub.Contracts.Persistence;
using DevHub.Contracts.Identity;

namespace DevHub.Modules.Workspace.Entities;

public sealed class Member : BaseEntity, ISoftDeletable
{
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    public MemberStatus Status { get; set; } = MemberStatus.Active;
    public DateTimeOffset? DeletedAt { get; set; }
}
