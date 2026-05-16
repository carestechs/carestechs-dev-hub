namespace DevHub.Contracts.Authorization;

/// <summary>
/// Cross-module read-side view of a member's project memberships + workspace-level roles.
/// Identity uses this to populate the JWT and <c>/api/auth/me</c>.
/// </summary>
public interface IProjectMembershipQuery
{
    Task<IReadOnlyList<MembershipDescriptor>> GetMembershipsAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// True if the member holds the system <c>operator</c> role at workspace scope.
    Task<bool> IsOperatorAsync(Guid memberId, CancellationToken cancellationToken = default);
}
