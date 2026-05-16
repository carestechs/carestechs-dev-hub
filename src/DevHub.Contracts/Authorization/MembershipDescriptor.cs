namespace DevHub.Contracts.Authorization;

/// <summary>
/// A member's tuple of <c>(project, roles)</c> for one project. Used by Identity to populate the JWT
/// and <c>/api/auth/me</c>.
/// </summary>
public sealed record MembershipDescriptor(
    Guid ProjectId,
    string ProjectSlug,
    IReadOnlyList<string> Roles);
