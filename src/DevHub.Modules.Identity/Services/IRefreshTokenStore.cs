namespace DevHub.Modules.Identity.Services;

public interface IRefreshTokenStore
{
    /// <summary>Issues a new refresh token for the given member. Returns the raw token (caller must transport it; we keep only the hash).</summary>
    Task<RefreshIssueResult> IssueAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>Rotates an existing refresh token. Returns a new token and revokes the old. Returns null if the input is unknown, expired, or already revoked (replay).</summary>
    Task<RefreshRotateResult?> RotateAsync(string rawToken, CancellationToken cancellationToken);

    /// <summary>Revokes the entire chain reachable from this token (replacements before and after).</summary>
    Task RevokeChainAsync(string rawToken, CancellationToken cancellationToken);
}

public sealed record RefreshIssueResult(string RawToken, DateTimeOffset ExpiresAt);

public sealed record RefreshRotateResult(Guid MemberId, string NewRawToken, DateTimeOffset NewExpiresAt);
