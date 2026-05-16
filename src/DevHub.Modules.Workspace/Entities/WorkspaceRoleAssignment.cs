using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Workspace.Entities;

/// <summary>
/// Workspace-level role grant — a member holds a role that is not scoped to any project
/// (e.g. <c>operator</c>). Distinct from <see cref="RoleAssignment"/>, which is scoped to a
/// <see cref="ProjectMembership"/>.
/// </summary>
public sealed class WorkspaceRoleAssignment : BaseEntity, ISoftDeletable
{
    public required Guid MemberId { get; set; }
    public required Guid RoleId { get; set; }
    public required Guid CreatedByMemberId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
