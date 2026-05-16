namespace DevHub.Contracts.Workspace;

/// <summary>
/// Cross-module role-key validator published by Workspace. Used by ExecutorRegistry to validate
/// that <c>CheckpointContract.requiredRoleKey</c> values reference real roles.
/// </summary>
public interface IRoleLookup
{
    Task<bool> ExistsAsync(string roleKey, CancellationToken cancellationToken = default);

    /// Given a set of role keys, returns the subset that do NOT exist (or an empty list).
    Task<IReadOnlyList<string>> GetMissingAsync(IEnumerable<string> roleKeys, CancellationToken cancellationToken = default);
}
