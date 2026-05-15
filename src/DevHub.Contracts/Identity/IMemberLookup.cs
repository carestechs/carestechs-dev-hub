namespace DevHub.Contracts.Identity;

/// <summary>
/// Cross-module lookup of a Workspace.Member by email. Implemented in
/// <c>DevHub.Modules.Workspace</c>; consumed by <c>DevHub.Modules.Identity</c>
/// so Identity never loads Workspace's entity directly.
/// </summary>
public interface IMemberLookup
{
    Task<MemberLookupResult?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<MemberLookupResult?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record MemberLookupResult(Guid Id, string DisplayName, string Email, MemberStatus Status);
