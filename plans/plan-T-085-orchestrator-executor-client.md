# Implementation Plan: T-085 — `OrchestratorExecutorClient`

## Task Reference
- **Task ID:** T-085 · **Type:** Backend · **Workflow:** standard · **Complexity:** L
- **Rationale:** AC-2..AC-8 + AC-10. The bulk of the FEAT.

## Overview
A new class implementing `IExecutorHttpClient` against the orchestrator's `/api/v1/runs` API. Includes status mapping, three-tier `currentCheckpointKey` + `currentTaskId` derivation, `assignments` projection from trace replay, and NDJSON-to-SSE conversion in `OpenStreamAsync`.

Implementation lives in `src/DevHub.Modules.WorkItems/Services/Orchestrator/`. The DI registration + factory selection lands in T-086.

## Implementation Steps

### Step 0: Verify the `awaiting_signal` field name
**Bash:**

```bash
grep -rn "awaiting_signal\|expected_signal\|signal_name" ../carestechs-agent-orchestrator/agents/ 2>/dev/null | head
```

Confirm the exact key the agent definitions use. If different from `awaiting_signal`, update tier-1 derivation below.

### Step 1: Status mapper
**File:** `src/DevHub.Modules.WorkItems/Services/Orchestrator/StatusMapper.cs` · Create

```csharp
namespace DevHub.Modules.WorkItems.Services.Orchestrator;

internal static class StatusMapper
{
    public static string MapRunStatus(string orchestratorStatus) => orchestratorStatus switch
    {
        "pending" or "running" => "Running",
        "paused" => "WaitingOnCheckpoint",
        "completed" => "Completed",
        "failed" => "Failed",
        "cancelled" => "Cancelled",
        _ => "Running",
    };
}
```

### Step 2: Checkpoint + task derivation
**File:** `src/DevHub.Modules.WorkItems/Services/Orchestrator/CheckpointDerivation.cs` · Create

```csharp
using System.Text.Json;

namespace DevHub.Modules.WorkItems.Services.Orchestrator;

internal static class CheckpointDerivation
{
    public static (string? CheckpointKey, string? TaskId) FromLastStep(JsonElement? lastStep)
    {
        if (lastStep is null || lastStep.Value.ValueKind != JsonValueKind.Object) return (null, null);
        if (!lastStep.Value.TryGetProperty("nodeInputs", out var inputs)) return (null, null);
        var signal = inputs.TryGetProperty("awaiting_signal", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() : null;
        var taskId = inputs.TryGetProperty("current_task_id", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() : null;
        return (signal, taskId);
    }

    public static (string? CheckpointKey, string? TaskId) FromTraceRecord(JsonElement traceRecord)
    {
        string? name = null;
        string? taskId = null;
        if (traceRecord.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
        if (name is null && traceRecord.TryGetProperty("signalName", out var s) && s.ValueKind == JsonValueKind.String)
            name = s.GetString();
        if (traceRecord.TryGetProperty("taskId", out var t) && t.ValueKind == JsonValueKind.String) taskId = t.GetString();
        return (name, taskId);
    }
}
```

The two-step derivation (tier 1, tier 2) is sequenced inside `FetchStateAsync` — see Step 5.

### Step 3: Executor-state projection (`assignments` from trace)
**File:** `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs` · Create

```csharp
using System.Text.Json;

namespace DevHub.Modules.WorkItems.Services.Orchestrator;

internal static class ExecutorStateProjection
{
    public static JsonElement Build(
        Guid runId,
        string agentRef,
        JsonElement? lastStep,
        IReadOnlyDictionary<string, string> assignments,
        string? stopReason)
    {
        using var doc = JsonDocument.Parse("{}");
        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        sb.Append($"\"runId\":\"{runId}\",");
        sb.Append($"\"agentRef\":{JsonSerializer.Serialize(agentRef)},");
        sb.Append("\"lastStep\":");
        sb.Append(lastStep is null ? "null" : lastStep.Value.GetRawText());
        sb.Append(",\"assignments\":");
        sb.Append(JsonSerializer.Serialize(assignments));
        sb.Append(",\"stopReason\":");
        sb.Append(stopReason is null ? "null" : JsonSerializer.Serialize(stopReason));
        sb.Append('}');
        return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
    }

    public static Dictionary<string, string> ParseAssignmentsFromTrace(IEnumerable<JsonElement> traceRecords)
    {
        var result = new Dictionary<string, string>();
        foreach (var rec in traceRecords)
        {
            if (!rec.TryGetProperty("kind", out var kind) || kind.GetString() != "signal") continue;
            if (!rec.TryGetProperty("name", out var name) || name.GetString() != "assignment-confirmed") continue;
            if (!rec.TryGetProperty("taskId", out var task) || task.ValueKind != JsonValueKind.String) continue;
            if (!rec.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) continue;
            if (!payload.TryGetProperty("assignee", out var assignee) || assignee.ValueKind != JsonValueKind.String) continue;
            var assigneeStr = assignee.GetString();
            if (string.IsNullOrEmpty(assigneeStr)) continue;
            result[task.GetString()!] = assigneeStr;
        }
        return result;
    }
}
```

### Step 4: NDJSON-to-SSE stream wrapper
**File:** `src/DevHub.Modules.WorkItems/Services/Orchestrator/NdjsonToSseStream.cs` · Create

A small `Stream` subclass that reads NDJSON lines from the upstream stream and emits `data: <json>\n\n` bytes to consumers. Uses a `StreamReader` for line splitting and a write-back buffer for the current frame.

(Implementation ~80 lines; skipped here for brevity. The pattern is: `ReadAsync` fills its output buffer from a transformation pipeline that pulls lines from the upstream `StreamReader`, validates JSON, wraps as SSE, copies to the output.)

Add a `: ready\n\n` heartbeat as the first frame.

### Step 5: The client
**File:** `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs` · Create

Skeleton (full impl follows the standard pattern):

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Executors;
using Microsoft.Extensions.Logging;

namespace DevHub.Modules.WorkItems.Services.Orchestrator;

internal sealed class OrchestratorExecutorClient(
    HttpClient http,
    IExecutorCredentialResolver creds,
    ILogger<OrchestratorExecutorClient> log) : IExecutorHttpClient
{
    // Method signature change in T-086 — for now match the existing interface so this
    // class compiles; T-086 swaps both impls in lockstep.

    public async Task<ExecutorStartResponse> StartAsync(
        ExecutorRegistrationDescriptor executor,
        string correlationMarker,
        JsonElement input,
        CodeSourcePayload? codeSource,
        CancellationToken cancellationToken = default)
    {
        // 1. Synthesize CreateRunRequest body.
        var workItem = new
        {
            id = correlationMarker,
            kind = "DEVHUB",
            content = input.GetRawText(),
        };
        var intake = codeSource is null
            ? (object)new { workItem }
            : new { workItem, codeSource };
        var body = new { agentRef = ResolveAgentRef(executor), intake };

        using var req = NewRequest(HttpMethod.Post, executor, "/api/v1/runs", body);
        var resp = await SendAsync<JsonElement>(executor, req, cancellationToken);
        var data = resp.GetProperty("data");
        var runId = data.GetProperty("id").GetGuid();

        var execState = ExecutorStateProjection.Build(
            runId, ResolveAgentRef(executor), lastStep: null, assignments: new Dictionary<string, string>(), stopReason: null);

        return new ExecutorStartResponse(
            CurrentStatus: "Running",
            CurrentCheckpointKey: null,
            ExecutorState: execState,
            CurrentTaskId: null);
    }

    public async Task<ExecutorFetchResponse> FetchStateAsync(
        ExecutorRegistrationDescriptor executor,
        string correlationMarker,
        CancellationToken cancellationToken = default)
    {
        // T-086 will pass a WorkItemRef; for now look up via a temporary helper.
        var runId = await LookupRunIdAsync(correlationMarker, cancellationToken);
        if (runId is null) throw new NotFoundException("Run not found for marker.");

        // 1. Fetch the run detail.
        using var detailReq = NewRequest(HttpMethod.Get, executor, $"/api/v1/runs/{runId}");
        var detailResp = await SendAsync<JsonElement>(executor, detailReq, cancellationToken);
        var detail = detailResp.GetProperty("data");
        var status = StatusMapper.MapRunStatus(detail.GetProperty("status").GetString()!);

        // 2. Derive checkpoint + task id (only when paused).
        string? checkpointKey = null;
        string? currentTaskId = null;
        if (status == "WaitingOnCheckpoint")
        {
            // Tier 1: lastStep
            JsonElement? lastStep = detail.TryGetProperty("lastStep", out var ls) && ls.ValueKind == JsonValueKind.Object
                ? ls : null;
            (checkpointKey, currentTaskId) = CheckpointDerivation.FromLastStep(lastStep);

            // Tier 2: trace scan
            if (checkpointKey is null)
            {
                var awaiting = await ScanTraceAsync(executor, runId.Value, kinds: new[] { "awaiting_signal" }, cancellationToken);
                var last = awaiting.LastOrDefault();
                if (last.ValueKind == JsonValueKind.Object)
                    (checkpointKey, currentTaskId) = CheckpointDerivation.FromTraceRecord(last);
            }

            if (checkpointKey is null)
                log.LogInformation("Run {RunId} paused but no awaiting_signal derived", runId);
        }

        // 3. Build executorState (one more trace scan for assignments).
        var signalRecords = await ScanTraceAsync(executor, runId.Value, kinds: new[] { "signal" }, cancellationToken);
        var assignments = ExecutorStateProjection.ParseAssignmentsFromTrace(signalRecords);
        var stopReason = detail.TryGetProperty("stopReason", out var sr) && sr.ValueKind == JsonValueKind.String
            ? sr.GetString() : null;
        var lastStepRaw = detail.TryGetProperty("lastStep", out var lsRaw) && lsRaw.ValueKind == JsonValueKind.Object
            ? (JsonElement?)lsRaw : null;
        var execState = ExecutorStateProjection.Build(runId.Value, ResolveAgentRef(executor), lastStepRaw, assignments, stopReason);

        return new ExecutorFetchResponse(status, checkpointKey, execState, currentTaskId);
    }

    public async Task<ExecutorSignalResponse> SignalAsync(
        ExecutorRegistrationDescriptor executor,
        string correlationMarker,
        string checkpointKey,
        string outcome,
        JsonElement? payload,
        string? taskId,
        CancellationToken cancellationToken = default)
    {
        var runId = await LookupRunIdAsync(correlationMarker, cancellationToken)
            ?? throw new NotFoundException("Run not found for marker.");

        var body = new { name = checkpointKey, taskId, payload = payload ?? JsonDocument.Parse("{}").RootElement };
        using var req = NewRequest(HttpMethod.Post, executor, $"/api/v1/runs/{runId}/signals", body);
        await SendAsync<JsonElement>(executor, req, cancellationToken);

        // Refresh state so the response carries up-to-date info.
        var fetched = await FetchStateAsync(executor, correlationMarker, cancellationToken);
        return new ExecutorSignalResponse(
            fetched.CurrentStatus, fetched.CurrentCheckpointKey, fetched.ExecutorState, HttpStatus: 200, fetched.CurrentTaskId);
    }

    public async Task<ExecutorStreamConnection> OpenStreamAsync(
        ExecutorRegistrationDescriptor executor,
        string correlationMarker,
        CancellationToken cancellationToken = default)
    {
        var runId = await LookupRunIdAsync(correlationMarker, cancellationToken)
            ?? throw new NotFoundException("Run not found for marker.");

        var req = NewRequest(HttpMethod.Get, executor, $"/api/v1/runs/{runId}/trace?follow=true");
        var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var status = (int)resp.StatusCode;
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            resp.Dispose();
            throw new ExecutorFailureException(executor.Id, executor.Key, NewCorrelationId(), status, body);
        }
        var upstream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        var wrapped = new NdjsonToSseStream(upstream);
        return new ExecutorStreamConnection(wrapped, resp);
    }

    public async Task CancelAsync(
        ExecutorRegistrationDescriptor executor,
        string correlationMarker,
        CancellationToken cancellationToken = default)
    {
        var runId = await LookupRunIdAsync(correlationMarker, cancellationToken)
            ?? throw new NotFoundException("Run not found for marker.");
        using var req = NewRequest(HttpMethod.Post, executor, $"/api/v1/runs/{runId}/cancel",
            body: new { reason = "DevHub operator cancel" });
        await SendAsync<JsonElement>(executor, req, cancellationToken);
    }

    // Helpers: NewRequest, SendAsync, ResolveAgentRef, LookupRunIdAsync, ScanTraceAsync — see Step 6.
}
```

### Step 6: Helpers
- `NewRequest(method, descriptor, path, body)` — mirror the existing `ExecutorHttpClient` helper: sets `X-API-Key` from `creds.Resolve(descriptor.Id)`, sets `X-DevHub-Correlation`, builds the URI from `descriptor.BaseUrl + path`, serializes `body` to JSON.
- `SendAsync<T>(descriptor, req, ct)` — sends; non-2xx → `ExecutorFailureException` with the upstream body.
- `ResolveAgentRef(descriptor)` — for v1, derive from `descriptor.Key` or a new env var. Simplest: use `descriptor.Key` directly (operators set the executor's `Key` to the agent ref string). Document the convention.
- `LookupRunIdAsync(correlationMarker, ct)` — **temporary** helper that queries the WorkItem row. Replaced in T-086 by passing the run id through the interface instead.
- `ScanTraceAsync(descriptor, runId, kinds, ct)` — one-shot GET `/api/v1/runs/{runId}/trace?kind=...&kind=...` (no `follow`), parse NDJSON, return list.

### Step 7: Verify build
**Bash:**

```bash
dotnet build src/DevHub.Modules.WorkItems --nologo 2>&1 | tail
```

Class compiles. DI registration in T-086. Tests in T-088.

## Files Affected
| File | Action |
|---|---|
| `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs` | Create |
| `src/DevHub.Modules.WorkItems/Services/Orchestrator/StatusMapper.cs` | Create |
| `src/DevHub.Modules.WorkItems/Services/Orchestrator/CheckpointDerivation.cs` | Create |
| `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs` | Create |
| `src/DevHub.Modules.WorkItems/Services/Orchestrator/NdjsonToSseStream.cs` | Create |

## Edge Cases & Risks
- **Two trace scans per `FetchStateAsync` call** (one for `awaiting_signal`, one for `signal`). For typical work items the lists are short; if it becomes a hotspot, combine into one scan that filters on the consumer side.
- **`lastStep` may be absent** when the run hasn't taken any step yet. Tier 1 returns `(null, null)`, falls through to tier 2 cleanly.
- **Trace records may not declare `kind` consistently.** The orchestrator's trace store likely has a stable contract; verify by tailing a real run's `/trace` endpoint during T-088's harness build. If a record lacks `kind`, treat it as miscellaneous and skip.
- **Agent ref convention.** Storing the ref as the executor's `Key` field works but conflates "stable identifier" and "agent version". v1 acceptable; if it becomes friction, add a dedicated `AgentRef` column to `ExecutorRegistration` in a future FEAT.

## Acceptance Verification
- [ ] Class implements all five `IExecutorHttpClient` methods.
- [ ] Status mapping table matches the brief.
- [ ] Tier 1 derivation reads `lastStep.nodeInputs.awaiting_signal` + `current_task_id`.
- [ ] Tier 2 derivation reads from `/runs/{id}/trace?kind=awaiting_signal`.
- [ ] `assignments` projection replays `assignment-confirmed` signals from `/trace?kind=signal`.
- [ ] NDJSON-to-SSE wrapper emits one frame per JSON line.
- [ ] `dotnet build` clean.
