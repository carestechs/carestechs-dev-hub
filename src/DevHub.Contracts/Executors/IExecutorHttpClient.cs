using System.Text.Json;

namespace DevHub.Contracts.Executors;

/// <summary>
/// The DevHub-to-executor HTTP boundary. Implementations resolve the bearer token via
/// <see cref="IExecutorCredentialResolver"/> per request; the resolved value MUST never be
/// stored, logged, or otherwise persisted.
///
/// On any non-2xx upstream response (or connection failure) implementations throw
/// <c>ExecutorFailureException</c> with the executor identity, a fresh correlation id, and
/// the upstream body (when present) — the global problem-details handler maps it to 502.
/// </summary>
public interface IExecutorHttpClient
{
    Task<ExecutorStartResponse> StartAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        JsonElement input,
        CodeSourcePayload? codeSource,
        CancellationToken cancellationToken = default);

    Task<ExecutorFetchResponse> FetchStateAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        CancellationToken cancellationToken = default);

    Task<ExecutorSignalResponse> SignalAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        string checkpointKey,
        string outcome,
        JsonElement? payload,
        string? taskId,
        CancellationToken cancellationToken = default);

    Task<ExecutorStreamConnection> OpenStreamAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        CancellationToken cancellationToken = default);

    Task CancelAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        CancellationToken cancellationToken = default);
}
