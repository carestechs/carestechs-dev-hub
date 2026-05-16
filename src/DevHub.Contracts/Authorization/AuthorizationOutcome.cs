namespace DevHub.Contracts.Authorization;

/// <summary>
/// Result of a single <see cref="IProjectAuthorizationService"/> check. The service has already
/// written an audit row by the time it returns this value.
/// </summary>
public sealed record AuthorizationOutcome(bool Granted, string? DeniedReason = null)
{
    public static AuthorizationOutcome Allow() => new(Granted: true);
    public static AuthorizationOutcome Deny(string reason) => new(Granted: false, DeniedReason: reason);
}
