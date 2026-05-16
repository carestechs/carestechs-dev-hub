namespace DevHub.Contracts.Authorization;

/// <summary>
/// Cross-module read-side view of a member's project memberships + workspace-level roles.
/// Identity uses this to populate the JWT and <c>/api/auth/me</c>; Notifications uses the
/// per-project / operator queries to compute the set of members responsible for a checkpoint.
/// </summary>
public interface IProjectMembershipQuery
{
    Task<IReadOnlyList<MembershipDescriptor>> GetMembershipsAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// True if the member holds the system <c>operator</c> role at workspace scope.
    Task<bool> IsOperatorAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// All members on <paramref name="projectId"/> whose role assignments include
    /// <paramref name="roleKey"/>. Used by the notifications reconciler to identify who is
    /// responsible for a checkpoint.
    Task<IReadOnlyList<Guid>> GetMembersWithRoleAsync(
        Guid projectId,
        string roleKey,
        CancellationToken cancellationToken = default);

    /// All members holding the workspace-scoped <c>operator</c> role.
    Task<IReadOnlyList<Guid>> GetWorkspaceOperatorsAsync(CancellationToken cancellationToken = default);
}
