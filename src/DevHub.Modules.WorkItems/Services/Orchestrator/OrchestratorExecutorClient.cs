using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Executors;
using Microsoft.Extensions.Logging;

namespace DevHub.Modules.WorkItems.Services.Orchestrator;

/// <summary>
/// FEAT-010 (T-085): <see cref="IExecutorHttpClient"/> implementation against the
/// carestechs-agent-orchestrator's <c>/api/v1/runs</c> API.
///
/// Outbound auth: <c>X-API-Key</c> header sourced from the executor's <c>CredentialsRef</c>.
/// </summary>
internal sealed class OrchestratorExecutorClient(
    HttpClient http,
    IExecutorCredentialResolver creds,
    ILogger<OrchestratorExecutorClient> log) : IExecutorHttpClient
{
    public async Task<ExecutorStartResponse> StartAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        JsonElement input,
        CodeSourcePayload? codeSource,
        CancellationToken cancellationToken = default)
    {
        var correlationMarker = workItem.Marker;
        // The orchestrator's CreateRunRequest expects { agentRef, intake: { workItem, codeSource? } }.
        // We synthesize the workItem from DevHub's marker + input.
        var workItemPayload = new
        {
            id = correlationMarker,
            kind = "DEVHUB",
            content = input.ValueKind == JsonValueKind.Undefined ? "{}" : input.GetRawText(),
        };
        object intake = codeSource is null
            ? new { workItem = workItemPayload }
            : new { workItem = workItemPayload, codeSource };
        var body = new { agentRef = ResolveAgentRef(executor), intake };

        using var req = NewRequest(HttpMethod.Post, executor, "/api/v1/runs");
        req.Content = JsonContent.Create(body);
        var resp = await SendJsonAsync<JsonElement>(executor, req, correlationMarker, cancellationToken);

        // Envelope: { data: RunSummaryDto, meta: ... }. Pull data.id as the run uuid.
        var runId = resp.GetProperty("data").GetProperty("id").GetGuid();

        var execState = ExecutorStateProjection.Build(
            runId,
            ResolveAgentRef(executor),
            lastStep: null,
            assignments: new Dictionary<string, string>(),
            stopReason: null);

        // Surface runId in executorState so WorkItemsService (T-086) can persist it on
        // WorkItem.ExecutorRunId.
        return new ExecutorStartResponse(
            CurrentStatus: "Running",
            CurrentCheckpointKey: null,
            ExecutorState: execState,
            CurrentTaskId: null);
    }

    public async Task<ExecutorFetchResponse> FetchStateAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        CancellationToken cancellationToken = default)
    {
        var correlationMarker = workItem.Marker;
        var runId = workItem.ExecutorRunId
            ?? throw new NotFoundException($"No orchestrator run id for marker '{correlationMarker}'.");

        using var detailReq = NewRequest(HttpMethod.Get, executor, $"/api/v1/runs/{runId}");
        var detailEnv = await SendJsonAsync<JsonElement>(executor, detailReq, correlationMarker, cancellationToken);
        var detail = detailEnv.GetProperty("data");

        var status = StatusMapper.MapRunStatus(detail.GetProperty("status").GetString()!);

        string? checkpointKey = null;
        string? currentTaskId = null;
        JsonElement? lastStep = detail.TryGetProperty("lastStep", out var ls) && ls.ValueKind == JsonValueKind.Object
            ? ls : null;

        if (status == "WaitingOnCheckpoint")
        {
            var nodeName = lastStep is null ? null
                : lastStep.Value.TryGetProperty("nodeName", out var nn) && nn.ValueKind == JsonValueKind.String
                    ? nn.GetString() : null;
            checkpointKey = CheckpointDerivation.SignalForNodeName(nodeName);
            if (checkpointKey is null)
                log.LogInformation("Run {RunId} paused on node {NodeName}; no signal-name convention applies",
                    runId, nodeName ?? "(null)");
        }

        // One trace scan for assignments + currentTaskId — fetched as JSON, parsed
        // record-by-record by the projection helper.
        var traceRecords = await ScanTraceAsync(executor, runId, correlationMarker, cancellationToken);
        var assignments = ExecutorStateProjection.ParseAssignmentsFromTrace(traceRecords);
        if (currentTaskId is null && status == "WaitingOnCheckpoint")
            currentTaskId = ExecutorStateProjection.LatestSignalTaskId(traceRecords);

        var stopReason = detail.TryGetProperty("stopReason", out var sr) && sr.ValueKind == JsonValueKind.String
            ? sr.GetString() : null;

        var execState = ExecutorStateProjection.Build(
            runId, ResolveAgentRef(executor), lastStep, assignments, stopReason);

        return new ExecutorFetchResponse(status, checkpointKey, execState, currentTaskId);
    }

    public async Task<ExecutorSignalResponse> SignalAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        string checkpointKey,
        string outcome,
        JsonElement? payload,
        string? taskId,
        CancellationToken cancellationToken = default)
    {
        var correlationMarker = workItem.Marker;
        var runId = workItem.ExecutorRunId
            ?? throw new NotFoundException($"No orchestrator run id for marker '{correlationMarker}'.");

        // The orchestrator routes by `name`. DevHub's `checkpointKey` IS the signal name.
        // `outcome` is DevHub-side bookkeeping (it's logged + audited on the DevHub side);
        // the orchestrator doesn't use it directly.
        _ = outcome;

        // The orchestrator's SignalCreateRequest body shape.
        var body = new
        {
            name = checkpointKey,
            taskId,
            payload = payload ?? JsonDocument.Parse("{}").RootElement,
        };

        using var req = NewRequest(HttpMethod.Post, executor, $"/api/v1/runs/{runId}/signals");
        req.Content = JsonContent.Create(body);
        await SendJsonAsync<JsonElement>(executor, req, correlationMarker, cancellationToken);

        // Refetch so DevHub gets the updated currentStatus / currentCheckpointKey.
        var refreshed = await FetchStateAsync(executor, workItem, cancellationToken);
        return new ExecutorSignalResponse(
            refreshed.CurrentStatus, refreshed.CurrentCheckpointKey,
            refreshed.ExecutorState, HttpStatus: 200, refreshed.CurrentTaskId);
    }

    public async Task<ExecutorStreamConnection> OpenStreamAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        CancellationToken cancellationToken = default)
    {
        var runId = workItem.ExecutorRunId
            ?? throw new NotFoundException($"No orchestrator run id for marker '{workItem.Marker}'.");

        var req = NewRequest(HttpMethod.Get, executor, $"/api/v1/runs/{runId}/trace?follow=true");
        await AuthAsync(req, executor.Id, cancellationToken);
        var correlationId = NewCorrelationId();
        HttpResponseMessage? resp = null;
        try
        {
            resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(resp, cancellationToken);
                var status = (int)resp.StatusCode;
                resp.Dispose();
                throw new ExecutorFailureException(executor.Id, executor.Key, correlationId, status, body);
            }
            var upstream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            var wrapped = new NdjsonToSseStream(upstream);
            return new ExecutorStreamConnection(wrapped, resp);
        }
        catch
        {
            resp?.Dispose();
            throw;
        }
    }

    public async Task CancelAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        CancellationToken cancellationToken = default)
    {
        var correlationMarker = workItem.Marker;
        var runId = workItem.ExecutorRunId
            ?? throw new NotFoundException($"No orchestrator run id for marker '{correlationMarker}'.");

        using var req = NewRequest(HttpMethod.Post, executor, $"/api/v1/runs/{runId}/cancel");
        req.Content = JsonContent.Create(new { reason = "DevHub operator cancel" });
        await SendJsonAsync<JsonElement>(executor, req, correlationMarker, cancellationToken);
    }

    // ---------- Helpers ----------

    /// <summary>
    /// The "agentRef" the orchestrator wants on POST /runs. v1 convention: stash it in the
    /// executor's <c>Key</c> field (operators set <c>Key = "lifecycle-agent@0.4.0-manual"</c>
    /// when registering). Future FEAT can add a dedicated AgentRef column if this is too
    /// coupled.
    /// </summary>
    private static string ResolveAgentRef(ExecutorRegistrationDescriptor executor) => executor.Key;

    /// <summary>
    /// One-shot trace scan (no <c>follow</c>) returning every JSON record.
    /// </summary>
    private async Task<List<JsonElement>> ScanTraceAsync(
        ExecutorRegistrationDescriptor executor, Guid runId, string correlationMarker, CancellationToken ct)
    {
        using var req = NewRequest(HttpMethod.Get, executor, $"/api/v1/runs/{runId}/trace");
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
            var raw = await resp.Content.ReadAsStringAsync(ct);
            var records = new List<JsonElement>();
            foreach (var line in raw.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    records.Add(doc.RootElement.Clone());
                }
                catch (JsonException)
                {
                    // Skip malformed lines — same semantics as the SSE forwarder.
                }
            }
            return records;
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning("Trace scan to executor {Key} for {Marker} failed: {Message}",
                executor.Key, correlationMarker, ex.Message);
            throw new ExecutorFailureException(executor.Id, executor.Key, correlationId, null, ex.Message);
        }
    }

    private HttpRequestMessage NewRequest(
        HttpMethod method, ExecutorRegistrationDescriptor executor, string relativePath)
    {
        var baseUri = new Uri(executor.BaseUrl.EndsWith('/') ? executor.BaseUrl : executor.BaseUrl + "/");
        var uri = new Uri(baseUri, relativePath.TrimStart('/'));
        return new HttpRequestMessage(method, uri);
    }

    private async Task AuthAsync(HttpRequestMessage req, Guid executorId, CancellationToken ct)
    {
        var token = await creds.ResolveAsync(executorId, ct);
        if (!string.IsNullOrEmpty(token))
        {
            // Orchestrator's auth is X-API-Key, not Bearer.
            req.Headers.TryAddWithoutValidation("X-API-Key", token);
        }
        // NEVER log the resolved value.
    }

    private async Task<T> SendJsonAsync<T>(
        ExecutorRegistrationDescriptor executor, HttpRequestMessage req, string correlationMarker, CancellationToken ct)
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
            log.LogWarning("Orchestrator {Key} unreachable: {Message}", executor.Key, ex.Message);
            throw new ExecutorFailureException(executor.Id, executor.Key, correlationId, null, ex.Message);
        }
        catch (JsonException ex)
        {
            log.LogWarning("Orchestrator {Key} returned unparseable JSON: {Message}", executor.Key, ex.Message);
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
