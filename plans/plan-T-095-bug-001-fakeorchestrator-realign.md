# Implementation Plan: T-095 — Realign FakeOrchestrator + existing test fixtures to the real shape

## Task Reference
- **Task ID:** T-095
- **Type:** Testing
- **Workflow:** standard
- **Complexity:** M
- **Dependencies:** T-094 (lands in the same PR).
- **Rationale:** The FakeOrchestrator harness currently mirrors DevHub's (wrong) reader instead of the orchestrator's (right) writer. After T-094 realigns DevHub to the real shape, the harness must follow. Doing both in one PR keeps `dotnet test` green throughout the merge.

## Overview
Rewrite `TraceRecord` as a `{Kind, Data}` representation with static helpers for the two common kinds (`Step`, `OperatorSignal`). Update the NDJSON emitter to serialize `{"kind": rec.Kind, "data": rec.Data}`. Update the 3 existing `new TraceRecord(...)` call sites in tests and the 1 auto-append in the harness itself.

## Implementation Steps

### Step 1: Rewrite `TraceRecord`
**File:** `tests/DevHub.TestHarness/FakeOrchestrator/ScriptedRunResponses.cs`
**Action:** Modify

- Replace the existing positional record (lines 39-43) with a discriminated representation:
  ```csharp
  /// <summary>
  /// Mirrors the orchestrator's on-the-wire NDJSON shape:
  /// <c>{"kind":"...","data":{...}}</c> per
  /// carestechs-agent-orchestrator/src/app/modules/ai/service.py § _serialize_trace_record.
  /// Use the static factories (<see cref="Step"/>, <see cref="OperatorSignal"/>) instead
  /// of constructing directly — they enforce the by-alias casing the orchestrator emits.
  /// </summary>
  public sealed record TraceRecord(string Kind, object Data)
  {
      /// <summary>kind="step" with the StepDto fields the orchestrator emits.</summary>
      public static TraceRecord Step(string nodeName, string status = "completed", string? taskId = null, int stepNumber = 1)
          => new("step", new
          {
              id = Guid.NewGuid(),
              stepNumber,
              nodeName,
              status,
              nodeInputs = taskId is null ? new object() : new { taskId },
          });

      /// <summary>kind="operator_signal" with the RunSignalDto fields the orchestrator emits.</summary>
      public static TraceRecord OperatorSignal(string name, string? taskId = null, object? payload = null)
          => new("operator_signal", new
          {
              id = Guid.NewGuid(),
              runId = Guid.NewGuid(),  // tests rarely assert on this; harness keeps it stable per call
              name,
              taskId,
              payload = payload ?? new { },
              receivedAt = DateTimeOffset.UtcNow,
              dedupeKey = Guid.NewGuid().ToString("N"),
          });
  }
  ```
- Notes:
  - `nodeInputs` is emitted as `{}` (empty object) when `taskId` is null, mirroring the orchestrator's `dict[str, Any]` default. **Not** absent — pydantic serialization always includes the field.
  - `Status` defaults to `"completed"` for `Step` because that's what tests usually want; override per call when needed.
  - The full `StepDto` schema includes more fields (`nodeResult`, `error`, `dispatchedAt`, `completedAt`), but DevHub's parser only reads `nodeInputs.taskId`. Tests stay minimal; if a future test needs more fields, extend the factory rather than construct `TraceRecord` directly.

### Step 2: Update the NDJSON emitter
**File:** `tests/DevHub.TestHarness/FakeOrchestrator/FakeOrchestratorHost.cs`
**Action:** Modify

- Find the emitter loop (currently `FakeOrchestratorHost.cs:158-168`). Replace the flat-shape serialization with the wrapped shape:
  ```csharp
  foreach (var rec in marker.Owner!.Scripted.TraceRecords)
  {
      var json = JsonSerializer.Serialize(new { kind = rec.Kind, data = rec.Data });
      var bytes = Encoding.UTF8.GetBytes(json + "\n");
      await ctx.Response.Body.WriteAsync(bytes, ctx.RequestAborted);
      await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
  }
  ```
- The default `System.Text.Json` policy preserves camelCase property names from the anonymous object (e.g. `nodeInputs`, `taskId`) — matches the orchestrator's pydantic `by_alias=True` output.

### Step 3: Update the signal auto-append in the harness
**File:** `tests/DevHub.TestHarness/FakeOrchestrator/FakeOrchestratorHost.cs`
**Action:** Modify

- At `FakeOrchestratorHost.cs:121`, the harness currently auto-appends an inbound signal to `TraceRecords` via `new TraceRecord("signal", name, taskId, payload)`. Replace with the new factory:
  ```csharp
  marker.Owner!.Scripted.TraceRecords.Add(TraceRecord.OperatorSignal(name, taskId, payload));
  ```

### Step 4: Update existing test fixtures
**File:** `tests/DevHub.Modules.WorkItems.Tests/OrchestratorExecutorClientTests.cs`
**Action:** Modify

- 3 call sites at lines 177-179. Currently:
  ```csharp
  new TraceRecord("signal", "assignment-confirmed", "T-001", new { assignee = "Alice" }),
  new TraceRecord("signal", "assignment-confirmed", "T-002", new { assignee = "Bob" }),
  new TraceRecord("signal", "tasks-confirmed", "T-001", null),  // ignored
  ```
- Replace with:
  ```csharp
  TraceRecord.OperatorSignal("assignment-confirmed", "T-001", new { assignee = "Alice" }),
  TraceRecord.OperatorSignal("assignment-confirmed", "T-002", new { assignee = "Bob" }),
  TraceRecord.OperatorSignal("tasks-confirmed", "T-001"),  // ignored — wrong name
  ```
- Verify by `grep -rn "new TraceRecord(" tests/` after — should return zero hits.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `tests/DevHub.TestHarness/FakeOrchestrator/ScriptedRunResponses.cs` | Modify | `TraceRecord` becomes `{Kind, Data}`; add `Step` / `OperatorSignal` factories. |
| `tests/DevHub.TestHarness/FakeOrchestrator/FakeOrchestratorHost.cs` | Modify | NDJSON emitter wraps under `data`; signal auto-append uses the factory. |
| `tests/DevHub.Modules.WorkItems.Tests/OrchestratorExecutorClientTests.cs` | Modify | 3 fixture lines switched to `TraceRecord.OperatorSignal(...)`. |

## Edge Cases & Risks

- **Existing test that asserts the old `{kind, name, taskId, payload}` byte shape.** Search for any test that introspects the NDJSON line shape (rather than just the parsed projection). Should be none — the existing tests assert on the *parsed* `ExecutorFetchResponse` / `executorState`. But if one exists, it was asserting on the harness's wrong shape and needs updating to the real shape.
- **`null` taskId in `OperatorSignal`.** Pydantic's `RunSignalDto.task_id: str | None` allows null. The factory emits `taskId: null` (System.Text.Json default for nullable string). Parser side: `ValueKind == JsonValueKind.String` skips null records — matches the existing skip behavior.
- **Stability of `receivedAt` / `id` / `runId` / `dedupeKey` for assertion-based tests.** None of DevHub's projections read these fields. Generating fresh GUIDs and `UtcNow` per call is fine.
- **`Step` factory's default `status = "completed"`.** DevHub doesn't read `status` off step records (yet). If a future projection does, override per call.

## Acceptance Verification

- [ ] `TraceRecord` has the new `{Kind, Data}` shape with `Step` and `OperatorSignal` factories.
- [ ] NDJSON emitter produces `{"kind":"step","data":{…}}` / `{"kind":"operator_signal","data":{…}}` — verify by hitting the FakeOrchestrator's trace endpoint with `curl` (or by reading the test output directly).
- [ ] All 3 existing `new TraceRecord(...)` call sites in `OrchestratorExecutorClientTests` use the new factories. `grep -rn "new TraceRecord(" tests/` returns zero hits.
- [ ] `dotnet build` green.
- [ ] `dotnet test` green for `OrchestratorExecutorClientTests` (the existing tests should now pass against T-094's realigned parser + this task's realigned harness). If they fail, debug as one unit — the two halves only work together.
