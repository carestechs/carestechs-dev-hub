# Implementation Plan: T-088 — Fake orchestrator + integration tests

## Task Reference
- **Task ID:** T-088 · **Type:** Testing · **Workflow:** standard · **Complexity:** M
- **Rationale:** Brief's quality bar. Without coverage, the next refactor breaks something silently.

## Overview
New `FakeOrchestratorHost` (sibling to `FakeExecutorHost`) listens on `/api/v1/runs/...` routes. Two new test classes drive `OrchestratorExecutorClient` against it: a unit-level class for the translation logic, and an end-to-end class that exercises start → fetch → signal → cancel + the FEAT-008 / FEAT-009 paths.

## Implementation Steps

### Step 1: Add scripted-response model
**File:** `tests/DevHub.TestHarness/FakeOrchestrator/ScriptedRunResponses.cs` · Create

Mirror the existing `ScriptedResponses` shape but for orchestrator-side responses:

```csharp
namespace DevHub.TestHarness.FakeOrchestrator;

public sealed class ScriptedRunResponses
{
    // Per-run-id state — tests can mutate to simulate transitions.
    public string CurrentRunStatus { get; set; } = "running";  // RunStatus values
    public string? CurrentAwaitingSignal { get; set; }         // for tier-1 derivation
    public string? CurrentTaskId { get; set; }
    public string? StopReason { get; set; }

    // Trace records emitted by the trace endpoint. Tests can mutate.
    public List<TraceRecord> TraceRecords { get; set; } = new();
}

public sealed record TraceRecord(
    string Kind,           // "signal" | "awaiting_signal" | "step" | etc.
    string? Name,          // for signals: "assignment-confirmed" etc.
    string? TaskId,
    object? Payload,
    object? NodeInputs);
```

### Step 2: The Kestrel host
**File:** `tests/DevHub.TestHarness/FakeOrchestrator/FakeOrchestratorHost.cs` · Create

Structure mirrors `FakeExecutorHost`:

- Hosts on `127.0.0.1:0` (random port).
- `Scripted` property exposes the mutable response model.
- `Calls` queue records every inbound request (path, method, body).
- Routes:
  - `POST /api/v1/runs` → assigns a new run_id (sequential or random), records the call, responds with `{ data: { id, agentRef, status: "running", startedAt }, meta: null }` (202).
  - `GET /api/v1/runs/{run_id}` → returns `{ data: { id, agentRef, status, stopReason, startedAt, intake, traceUri, stepCount, lastStep } }`.
  - `POST /api/v1/runs/{run_id}/signals` → appends a `TraceRecord(kind="signal", name=body.name, taskId, payload)` to the scripted list; returns `{ data: SignalDto, meta: null }` (202).
  - `POST /api/v1/runs/{run_id}/cancel` → sets `CurrentRunStatus = "cancelled"`, returns `{ data: ... }` (200).
  - `GET /api/v1/runs/{run_id}/trace` → emits NDJSON. With `follow=true` keep the connection open (yield records as they're added — for v1, just emit existing records and close); without `follow`, dump all current records and close.

### Step 3: Wire into `DevHubApiFactory`
**File:** `tests/DevHub.TestHarness/DevHubApiFactory.cs` · Modify

Add a `UseFakeOrchestrator` flag (mutually exclusive with `UseFakeExecutor`). When true: boot `FakeOrchestratorHost` and seed an executor registration with `Protocol = "orchestrator"`, `BaseUrl = <fake-host-url>`, `Key = "lifecycle-agent@0.4.0-manual"`.

### Step 4: Translator unit tests
**File:** `tests/DevHub.Modules.WorkItems.Tests/OrchestratorExecutorClientTests.cs` · Create

```csharp
[Collection("postgres")]
public class OrchestratorExecutorClientTests : IAsyncLifetime
{
    // Standard fixture setup — see CodeSourceForwardTests / AssignmentSignalTests for the pattern.

    [Theory]
    [InlineData("pending", "Running")]
    [InlineData("running", "Running")]
    [InlineData("paused", "WaitingOnCheckpoint")]
    [InlineData("completed", "Completed")]
    [InlineData("failed", "Failed")]
    [InlineData("cancelled", "Cancelled")]
    public async Task FetchState_maps_RunStatus_to_CurrentStatus(string orchestratorStatus, string expected)
    {
        _fakeOrchestrator.Scripted.CurrentRunStatus = orchestratorStatus;
        var workItemId = await StartWorkItemAsync();

        var resp = await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{workItemId}");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("currentStatus").GetString().Should().Be(expected);
    }

    [Fact]
    public async Task CurrentCheckpointKey_derived_from_lastStep_when_present()
    {
        _fakeOrchestrator.Scripted.CurrentRunStatus = "paused";
        _fakeOrchestrator.Scripted.LastStepNodeInputs = new { awaiting_signal = "assignment-confirmed", current_task_id = "T-001" };
        // ...
        // assert currentCheckpointKey="assignment-confirmed" + currentTaskId="T-001"
    }

    [Fact]
    public async Task CurrentCheckpointKey_falls_back_to_trace_scan_when_lastStep_lacks_field()
    {
        _fakeOrchestrator.Scripted.CurrentRunStatus = "paused";
        _fakeOrchestrator.Scripted.TraceRecords.Add(new TraceRecord("awaiting_signal", "plan-confirmed", "T-002", null, null));
        // ...
        // assert currentCheckpointKey="plan-confirmed" + currentTaskId="T-002"
    }

    [Fact]
    public async Task CurrentCheckpointKey_returns_null_when_no_signal_can_be_derived()
    {
        _fakeOrchestrator.Scripted.CurrentRunStatus = "paused";
        // No lastStep, no trace records.
        // ...
        // assert currentCheckpointKey=null
    }

    [Fact]
    public async Task ExecutorState_assignments_replayed_from_trace()
    {
        _fakeOrchestrator.Scripted.TraceRecords.AddRange(new[]
        {
            new TraceRecord("signal", "assignment-confirmed", "T-001", new { assignee = "Alice" }, null),
            new TraceRecord("signal", "assignment-confirmed", "T-002", new { assignee = "Bob" }, null),
            new TraceRecord("signal", "tasks-confirmed", "T-001", null, null),  // ignored
        });
        // ...
        // assert executorState.assignments == { "T-001": "Alice", "T-002": "Bob" }
    }
}
```

### Step 5: End-to-end tests
**File:** `tests/DevHub.Modules.WorkItems.Tests/OrchestratorExecutorEndToEndTests.cs` · Create

Tests:
- **Start forwards intake.codeSource** (FEAT-008): seed project with `repo` + `defaultBranch`, start, assert fake orchestrator's recorded body has `intake.codeSource.repo = "..."`.
- **Start synthesizes intake.workItem**: assert `intake.workItem.id = marker`, `intake.workItem.content` is a JSON-encoded version of DevHub's input.
- **Start persists ExecutorRunId**: after start, query the WorkItem row, assert `ExecutorRunId` is non-null and matches the run id from the fake's response.
- **Signal forwards correctly**: POST signal with `outcome=confirmed, payload={assignee=Alice}, taskId=T-001`. Assert recorded body on `/runs/{id}/signals` has `{ name="confirmed", taskId="T-001", payload={assignee="Alice"} }`. (Note: this test reveals a subtle nuance — DevHub's `checkpointKey` becomes the orchestrator's `name`, NOT DevHub's `outcome`. Verify and document.)

Actually, looking at the signal translator plan (T-085): `name = checkpointKey`. So the URL's checkpoint key is the name. Let me adjust the assertion:

```csharp
[Fact]
public async Task Signal_forwards_checkpointKey_as_name()
{
    var workItemId = await StartWorkItemAsync();
    _fakeOrchestrator.ResetCalls();
    await _operator.PostAsJsonAsync(
        $"/api/projects/{_projectId}/work-items/{workItemId}/checkpoints/assignment-confirmed/signal",
        new { outcome = "confirmed", payload = new { assignee = "Alice" }, taskId = "T-001" });

    var signal = _fakeOrchestrator.Calls.Single(c => c.Path.EndsWith("/signals"));
    var body = JsonDocument.Parse(signal.BodyJson!).RootElement;
    body.GetProperty("name").GetString().Should().Be("assignment-confirmed");
    body.GetProperty("taskId").GetString().Should().Be("T-001");
    body.GetProperty("payload").GetProperty("assignee").GetString().Should().Be("Alice");
}
```

- **Cancel forwards correctly**: POST cancel, assert recorded call on `/runs/{id}/cancel`.
- **Audit invariants**: workitem:start + workitem:signal audit rows have the right details.

### Step 6: NDJSON-to-SSE conversion test
**File:** `tests/DevHub.Modules.WorkItems.Tests/OrchestratorStreamTests.cs` · Create

- Open the SSE stream on a work item whose fake orchestrator has scripted trace records.
- Assert: first frame is `: ready\n\n`, subsequent frames are `data: <json>\n\n` per scripted record, malformed lines suppressed.
- Test client disconnect closes upstream connection.

### Step 7: Run the suite
**Bash:**

```bash
dotnet test --nologo
```

Aim for 190 + ~15 new tests = ~205, all green.

## Files Affected
| File | Action |
|---|---|
| `tests/DevHub.TestHarness/FakeOrchestrator/FakeOrchestratorHost.cs` | Create |
| `tests/DevHub.TestHarness/FakeOrchestrator/ScriptedRunResponses.cs` | Create |
| `tests/DevHub.TestHarness/DevHubApiFactory.cs` | Modify (UseFakeOrchestrator flag) |
| `tests/DevHub.Modules.WorkItems.Tests/OrchestratorExecutorClientTests.cs` | Create |
| `tests/DevHub.Modules.WorkItems.Tests/OrchestratorExecutorEndToEndTests.cs` | Create |
| `tests/DevHub.Modules.WorkItems.Tests/OrchestratorStreamTests.cs` | Create |

## Edge Cases & Risks
- **`UseFakeOrchestrator` mutual exclusion.** Add a guard in `DevHubApiFactory`: setting both `UseFakeExecutor` + `UseFakeOrchestrator` is a configuration error. Throw on construction.
- **The fake host's `follow=true` semantics.** For unit tests, emit current records + close. For a true streaming smoke, add an explicit "long-poll" mode the test toggles.
- **NDJSON parsing in the test harness.** The host emits NDJSON via `Response.WriteAsync` line-by-line; the client (`OrchestratorExecutorClient.OpenStreamAsync`) reads it via its NdjsonToSseStream wrapper. Verify line boundaries via a fixture with known frame counts.
- **Trace records order.** The fake emits in append order; the derivation logic in `OrchestratorExecutorClient` takes the last `awaiting_signal` and the union of `assignment-confirmed` signals — order-independent for the final result.

## Acceptance Verification
- [ ] FakeOrchestratorHost handles all five routes.
- [ ] OrchestratorExecutorClientTests: ≥ 8 tests covering status mapping + checkpoint derivation + assignments.
- [ ] OrchestratorExecutorEndToEndTests: ≥ 5 tests covering start + signal + cancel + audit + codeSource forwarding.
- [ ] OrchestratorStreamTests: ≥ 3 tests covering frame conversion + heartbeat + malformed-line suppression.
- [ ] All 190 + ~15 new tests pass.
- [ ] Existing FakeExecutor tests untouched.
