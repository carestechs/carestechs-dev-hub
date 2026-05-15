namespace DevHub.Contracts.Identity;

/// <summary>
/// Resolves the calling member's identity from <c>HttpContext.User</c>. Registered
/// as scoped per request. Always non-null; check <see cref="IsAuthenticated"/>.
/// </summary>
public interface ICurrentMember
{
    bool IsAuthenticated { get; }

    /// Throws <c>InvalidOperationException</c> if <see cref="IsAuthenticated"/> is false.
    Guid MemberId { get; }
}
