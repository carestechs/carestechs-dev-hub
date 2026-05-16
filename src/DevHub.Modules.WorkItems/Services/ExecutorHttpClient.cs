using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Executors;
using Microsoft.Extensions.Logging;

namespace DevHub.Modules.WorkItems.Services;

internal sealed class ExecutorHttpClient(
    HttpClient http,
    IExecutorCredentialResolver creds,
    ILogger<ExecutorHttpClient> log) : IExecutorHttpClient
{
    public async Task<ExecutorStartResponse> StartAsync(
        ExecutorRegistrationDescriptor executor, string correlationMarker, JsonElement input, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, executor, correlationMarker, "/work-items");
        req.Content = JsonContent.Create(new { input, correlationMarker });
        return await SendJsonAsync<ExecutorStartResponse>(executor, req, ct);
    }

    public async Task<ExecutorFetchResponse> FetchStateAsync(
        ExecutorRegistrationDescriptor executor, string correlationMarker, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Get, executor, correlationMarker, $"/work-items/{correlationMarker}");
        return await SendJsonAsync<ExecutorFetchResponse>(executor, req, ct);
    }

    public async Task<ExecutorSignalResponse> SignalAsync(
        ExecutorRegistrationDescriptor executor, string correlationMarker, string checkpointKey,
        string outcome, JsonElement? payload, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, executor, correlationMarker,
            $"/work-items/{correlationMarker}/checkpoints/{Uri.EscapeDataString(checkpointKey)}/signal");
        req.Content = JsonContent.Create(new { outcome, payload });
        return await SendJsonAsync<ExecutorSignalResponse>(executor, req, ct);
    }

    public async Task<ExecutorStreamConnection> OpenStreamAsync(
        ExecutorRegistrationDescriptor executor, string correlationMarker, CancellationToken ct = default)
    {
        var req = NewRequest(HttpMethod.Get, executor, correlationMarker,
            $"/work-items/{correlationMarker}/stream");
        await AuthAsync(req, executor.Id, ct);
        var correlationId = NewCorrelationId();
        HttpResponseMessage? resp = null;
        try
        {
            resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(resp, ct);
                resp.Dispose();
                throw new ExecutorFailureException(executor.Id, executor.Key, correlationId,
                    (int)resp.StatusCode, body);
            }
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            // Ownership transfers to the caller via the returned connection.
            return new ExecutorStreamConnection(stream, resp);
        }
        catch (HttpRequestException ex)
        {
            resp?.Dispose();
            log.LogWarning("Executor {Key} unreachable for stream {Marker}: {Message}",
                executor.Key, correlationMarker, ex.Message);
            throw new ExecutorFailureException(executor.Id, executor.Key, correlationId, null, ex.Message);
        }
    }

    public async Task CancelAsync(
        ExecutorRegistrationDescriptor executor, string correlationMarker, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, executor, correlationMarker,
            $"/work-items/{correlationMarker}/cancel");
        await AuthAsync(req, executor.Id, ct);
        var correlationId = NewCorrelationId();
        try
        {
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(resp, ct);
                throw new ExecutorFailureException(executor.Id, executor.Key, correlationId,
                    (int)resp.StatusCode, body);
            }
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning("Executor {Key} unreachable for cancel {Marker}: {Message}",
                executor.Key, correlationMarker, ex.Message);
            throw new ExecutorFailureException(executor.Id, executor.Key, correlationId, null, ex.Message);
        }
    }

    private HttpRequestMessage NewRequest(
        HttpMethod method, ExecutorRegistrationDescriptor executor, string correlationMarker, string relativePath)
    {
        var baseUri = new Uri(executor.BaseUrl.EndsWith('/') ? executor.BaseUrl : executor.BaseUrl + "/");
        var uri = new Uri(baseUri, relativePath.TrimStart('/'));
        var req = new HttpRequestMessage(method, uri);
        req.Headers.TryAddWithoutValidation("X-DevHub-Correlation", correlationMarker);
        return req;
    }

    private async Task AuthAsync(HttpRequestMessage req, Guid executorId, CancellationToken ct)
    {
        var token = await creds.ResolveAsync(executorId, ct);
        if (!string.IsNullOrEmpty(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        // NEVER log the resolved value. Logging here would defeat the leak-test in T-038.
    }

    private async Task<T> SendJsonAsync<T>(
        ExecutorRegistrationDescriptor executor, HttpRequestMessage req, CancellationToken ct)
    {
        await AuthAsync(req, executor.Id, ct);
        var correlationId = NewCorrelationId();
        try
        {
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(resp, ct);
                throw new ExecutorFailureException(executor.Id, executor.Key, correlationId,
                    (int)resp.StatusCode, body);
            }
            var result = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            return result ?? throw new ExecutorFailureException(executor.Id, executor.Key, correlationId,
                (int)resp.StatusCode, "Empty response body.");
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning("Executor {Key} unreachable: {Message}", executor.Key, ex.Message);
            throw new ExecutorFailureException(executor.Id, executor.Key, correlationId, null, ex.Message);
        }
        catch (JsonException ex)
        {
            log.LogWarning("Executor {Key} returned unparseable JSON: {Message}", executor.Key, ex.Message);
            throw new ExecutorFailureException(executor.Id, executor.Key, correlationId, null, ex.Message);
        }
    }

    private static string NewCorrelationId() => Guid.NewGuid().ToString("N");

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return null; }
    }
}
