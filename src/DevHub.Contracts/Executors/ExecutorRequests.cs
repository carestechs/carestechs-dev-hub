using System.Text.Json;

namespace DevHub.Contracts.Executors;

/// Response shape from the executor's <c>POST /work-items</c>. The executor's full state lives
/// in <see cref="ExecutorState"/> as an opaque JSON blob; DevHub only caches the two flags.
public sealed record ExecutorStartResponse(
    string CurrentStatus,
    string? CurrentCheckpointKey,
    JsonElement ExecutorState);

public sealed record ExecutorFetchResponse(
    string CurrentStatus,
    string? CurrentCheckpointKey,
    JsonElement ExecutorState);

public sealed record ExecutorSignalResponse(
    string CurrentStatus,
    string? CurrentCheckpointKey,
    JsonElement ExecutorState,
    int HttpStatus);

/// Owns both the upstream HTTP response and its response stream. Disposing closes the upstream
/// socket — the stream forwarder MUST dispose this when the client disconnects.
public sealed class ExecutorStreamConnection(Stream stream, IDisposable response) : IAsyncDisposable
{
    public Stream Stream { get; } = stream;
    public ValueTask DisposeAsync()
    {
        Stream.Dispose();
        response.Dispose();
        return ValueTask.CompletedTask;
    }
}
