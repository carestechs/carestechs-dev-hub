# Implementation Plan: T-035 — IExecutorHttpClient + fake executor

## Task Reference
- **Task ID:** T-035 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-1 requires asserting *absence* of outbound HTTP on deny. Only a real fake executor with a call counter can prove that. AC-4 requires real chunked SSE arrival. The abstraction also keeps T-036's service-layer code testable.

## Overview
- Publish `IExecutorHttpClient` in `DevHub.Contracts`.
- Production impl in `DevHub.Modules.WorkItems/Services/ExecutorHttpClient.cs`, registered via `AddHttpClient`.
- `ExecutorFailureException` published from Contracts, mapped to `/probs/executor-failure` (502) by the existing middleware.
- `tests/DevHub.TestHarness/FakeExecutor/*` — `WebApplication`-hosted fake on a random local port, scriptable per call, exposes `Calls` counter.
- `DevHubApiFactory.WithFakeExecutor()` opt-in seeds the matching `ExecutorRegistration` + `ExecutorBinding` and rewires the base URL.

## Implementation Steps

### Step 1: Contract types
**Files (Create):**
- `src/DevHub.Contracts/Executors/IExecutorHttpClient.cs`
- `src/DevHub.Contracts/Executors/ExecutorRequests.cs` (request/response record types)
- `src/DevHub.Contracts/ApplicationErrors/ExecutorFailureException.cs`

```csharp
public interface IExecutorHttpClient
{
    Task<ExecutorStartResponse> StartAsync(ExecutorRegistrationDescriptor executor, string correlationMarker, JsonElement input, CancellationToken ct);
    Task<ExecutorFetchResponse> FetchStateAsync(ExecutorRegistrationDescriptor executor, string correlationMarker, CancellationToken ct);
    Task<ExecutorSignalResponse> SignalAsync(ExecutorRegistrationDescriptor executor, string correlationMarker, string checkpointKey, string outcome, JsonElement? payload, CancellationToken ct);
    Task<Stream> OpenStreamAsync(ExecutorRegistrationDescriptor executor, string correlationMarker, CancellationToken ct);
    Task CancelAsync(ExecutorRegistrationDescriptor executor, string correlationMarker, CancellationToken ct);
}

public sealed record ExecutorStartResponse(string CurrentStatus, string? CurrentCheckpointKey, JsonElement ExecutorState);
public sealed record ExecutorFetchResponse(string CurrentStatus, string? CurrentCheckpointKey, JsonElement ExecutorState);
public sealed record ExecutorSignalResponse(string CurrentStatus, string? CurrentCheckpointKey, JsonElement ExecutorState, int HttpStatus);

public sealed class ExecutorFailureException(Guid executorId, string executorKey, string correlationId, int? upstreamStatus, string? upstreamBody)
    : DomainException(
        title: "Executor failure",
        detail: $"Executor '{executorKey}' refused or was unreachable.",
        status: 502,
        type: "/probs/executor-failure")
{
    public Guid ExecutorId => executorId;
    public string ExecutorKey => executorKey;
    public string CorrelationId => correlationId;
    public int? UpstreamStatus => upstreamStatus;
    public string? UpstreamBody => upstreamBody;
}
```

### Step 2: Production impl
**File:** `src/DevHub.Modules.WorkItems/Services/ExecutorHttpClient.cs` · Create

```csharp
internal sealed class ExecutorHttpClient(HttpClient http, IExecutorCredentialResolver creds, ILogger<ExecutorHttpClient> log) : IExecutorHttpClient
{
    public async Task<ExecutorStartResponse> StartAsync(ExecutorRegistrationDescriptor e, string marker, JsonElement input, CancellationToken ct)
    {
        using var req = NewRequest(HttpMethod.Post, e, marker, $"/work-items");
        req.Content = JsonContent.Create(new { input });
        return await SendJsonAsync<ExecutorStartResponse>(e, marker, req, ct);
    }
    // FetchStateAsync: GET /work-items/{marker}
    // SignalAsync: POST /work-items/{marker}/checkpoints/{key}/signal
    // CancelAsync: POST /work-items/{marker}/cancel
    // OpenStreamAsync: GET /work-items/{marker}/stream with ResponseHeadersRead
    private HttpRequestMessage NewRequest(HttpMethod method, ExecutorRegistrationDescriptor e, string marker, string relativePath)
    {
        var uri = new Uri(new Uri(e.BaseUrl), relativePath);
        var req = new HttpRequestMessage(method, uri);
        req.Headers.Add("X-DevHub-Correlation", marker);
        return req;
    }
    private async Task AuthAsync(HttpRequestMessage req, Guid execId, CancellationToken ct)
    {
        var token = await creds.ResolveAsync(execId, ct);
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
    private async Task<T> SendJsonAsync<T>(ExecutorRegistrationDescriptor e, string marker, HttpRequestMessage req, CancellationToken ct)
    {
        await AuthAsync(req, e.Id, ct);
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                throw new ExecutorFailureException(e.Id, e.Key, correlationId, (int)resp.StatusCode, body);
            }
            var result = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            return result ?? throw new ExecutorFailureException(e.Id, e.Key, correlationId, null, "Empty response");
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Executor {Key} unreachable for {Marker}", e.Key, marker);
            throw new ExecutorFailureException(e.Id, e.Key, correlationId, null, ex.Message);
        }
    }
}
```

`OpenStreamAsync` opens with `HttpCompletionOption.ResponseHeadersRead` and returns `await resp.Content.ReadAsStreamAsync(ct)`. The caller (`WorkItemStreamForwarder` in T-037) owns disposal of both the response and the stream — wrap the returned stream in a tiny `OwningStream` that disposes the response on dispose, or refactor the signature to return `IAsyncDisposable` + `Stream`.

### Step 3: DI wiring
**File:** `src/DevHub.Modules.WorkItems/WorkItemsModuleExtensions.cs` · Modify
```csharp
services.AddHttpClient<IExecutorHttpClient, ExecutorHttpClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30); // applies to non-streaming; OpenStream uses CancellationToken
});
```

### Step 4: Problem-detail mapping
**File:** `src/DevHub.Api/Middleware/ProblemDetailsMiddleware.cs` · Modify
Add the `ExecutorFailureException` branch:
```csharp
ExecutorFailureException ex => Write(ctx, 502, "/probs/executor-failure", "Executor failure", ex.Detail,
    new { ex.ExecutorId, ex.ExecutorKey, ex.CorrelationId, upstreamStatus = ex.UpstreamStatus, details = ex.UpstreamBody }),
```

### Step 5: Fake executor
**Files (Create):**
- `tests/DevHub.TestHarness/FakeExecutor/FakeExecutorHost.cs`
- `tests/DevHub.TestHarness/FakeExecutor/ScriptedResponse.cs`
- `tests/DevHub.TestHarness/FakeExecutor/CallRecord.cs`

`FakeExecutorHost` builds a `WebApplication` bound to `http://127.0.0.1:0`. Endpoints:
- `POST /work-items` → records call, returns the scripted `start` response.
- `GET /work-items/{marker}` → returns the scripted fetch response.
- `POST /work-items/{marker}/checkpoints/{key}/signal` → returns scripted signal response.
- `GET /work-items/{marker}/stream` → writes scripted chunks with configurable delay.
- `POST /work-items/{marker}/cancel` → returns 204.

`CallRecord` columns: `Method`, `Path`, `BodyJson`, `OccurredAt`. `Calls` is a thread-safe `ConcurrentQueue<CallRecord>`. Helpers: `CountByPath(string)`, `Total`, `Reset()`.

```csharp
public sealed class FakeExecutorHost : IAsyncDisposable
{
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public int Port { get; private set; }
    public ConcurrentQueue<CallRecord> Calls { get; } = new();
    public ScriptedResponses Scripted { get; } = new();
    public static async Task<FakeExecutorHost> StartAsync(CancellationToken ct = default) { /* WebApplication.Run on :0, read Url, store port */ }
    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
```

`ScriptedResponses` has settable defaults + per-marker overrides for start/fetch/signal/stream/cancel.

### Step 6: Factory opt-in
**File:** `tests/DevHub.TestHarness/DevHubApiFactory.cs` · Modify

```csharp
public bool UseFakeExecutor { get; init; } = false;
private FakeExecutorHost? _fake;
public FakeExecutorHost Fake => _fake ?? throw new InvalidOperationException("Call WithFakeExecutor(...) first.");

protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    if (UseFakeExecutor)
    {
        _fake = FakeExecutorHost.StartAsync().GetAwaiter().GetResult();
        // Override TestRegistrySeeder so the seeded ExecutorRegistration's BaseUrl points at _fake.BaseUrl
        builder.ConfigureServices(s => s.AddHostedService(_ => new TestRegistrySeeder(_fake.BaseUrl)));
    }
    // existing config...
}
public override async ValueTask DisposeAsync()
{
    if (_fake is not null) await _fake.DisposeAsync();
    await base.DisposeAsync();
}
```

`TestRegistrySeeder` gains a `baseUrlOverride` constructor parameter — falls back to its existing default.

## Files Affected
| File | Action |
|------|--------|
| `Contracts/Executors/IExecutorHttpClient.cs`, `ExecutorRequests.cs` | Create |
| `Contracts/ApplicationErrors/ExecutorFailureException.cs` | Create |
| `WorkItems/Services/ExecutorHttpClient.cs` | Create |
| `WorkItems/WorkItemsModuleExtensions.cs` | Modify |
| `Api/Middleware/ProblemDetailsMiddleware.cs` | Modify |
| `TestHarness/FakeExecutor/*.cs` | Create |
| `TestHarness/DevHubApiFactory.cs` | Modify |

## Edge Cases & Risks
- **Resolved-credential leak via logs.** `ExecutorHttpClient` MUST NOT log the resolved token. Verified by an explicit test in T-038 that sets a known secret env var and greps every log line.
- **Stream cancellation.** If the test client disconnects, `HttpContext.RequestAborted` cancels the inner `CopyToAsync` — but the fake's writer needs to honor cancellation too, else the test hangs. Use `await Response.Body.WriteAsync(buf, ct)` in the fake stream endpoint.
- **`HttpClient.Timeout = 30s` on streaming.** Setting it globally on the typed client would kill SSE. Workaround: per-request `CancellationToken` is the only timeout for `OpenStreamAsync`; the global timeout is bypassed by passing `HttpCompletionOption.ResponseHeadersRead` + an unbounded CTS. Document this.

## Acceptance Verification
- [ ] `dotnet build` clean.
- [ ] Existing 71/71 backend tests stay green.
- [ ] Manual smoke: start the fake host in a tiny console, hit each endpoint with `curl`, confirm call records populate.
