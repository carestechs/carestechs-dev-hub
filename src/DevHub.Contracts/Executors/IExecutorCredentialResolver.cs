namespace DevHub.Contracts.Executors;

/// <summary>
/// Resolves an executor's <c>credentialsRef</c> to its current value (typically by reading an env
/// var). The resolved value MUST be used transiently by the caller and never logged, persisted,
/// or returned in any API response.
/// </summary>
public interface IExecutorCredentialResolver
{
    Task<string?> ResolveAsync(Guid executorId, CancellationToken cancellationToken = default);
}
