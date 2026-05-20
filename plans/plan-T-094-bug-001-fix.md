# Implementation Plan: T-094 — Realign `ExecutorStateProjection` to the real orchestrator trace shape

## Task Reference
- **Task ID:** T-094
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** M
- **Dependencies:** T-093 (complete — findings recorded in BUG-001 §6 and `plans/plan-T-093-bug-001-investigation.md`).
- **Rationale:** Rewrite DevHub's trace parser to read the on-the-wire `{kind, data}` shape with `kind ∈ {step, operator_signal}` — the contract empirically established by `carestechs-agent-orchestrator/tests/test_cli_runs.py:246-256` and `tests/integration/test_lifecycle_anthropic_mocked.py:239`. Fixes both broken projections (`currentTaskId` and `assignments`) at the source.

## Overview
Add `LatestStepTaskId` (reads `rec.data.nodeInputs.taskId` from the most recent `step`-kind record). Rewrite `ParseAssignmentsFromTrace` (filters on `kind == "operator_signal"`, reads `data.name` / `data.taskId` / `data.payload.assignee`). Delete `LatestSignalTaskId`. Swap the fallback in `OrchestratorExecutorClient.FetchStateAsync`. This is **production-code only** — the harness realignment is T-095, regression tests are T-096; the three tasks ship together in one PR.

## Implementation Steps

### Step 1: Add `LatestStepTaskId` to `ExecutorStateProjection`
**File:** `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs`
**Action:** Modify

- Add a new public static helper:
  ```csharp
  /// <summary>
  /// Returns the most recent <c>step</c>-kind trace record's <c>data.nodeInputs.taskId</c>,
  /// or <c>null</c> when no step has been dispatched or the latest step lacks the field.
  ///
  /// This is DevHub's primary derivation for <c>currentTaskId</c> in the orchestrator
  /// protocol: the orchestrator's deterministic runtime injects
  /// <c>nodeInputs.taskId = LifecycleMemory.current_task_id</c> at dispatch time
  /// (carestechs-agent-orchestrator/src/app/modules/ai/runtime_deterministic.py:246),
  /// so the latest step's <c>nodeInputs.taskId</c> IS the awaited task id at a pause.
  ///
  /// On-the-wire trace shape is <c>{"kind":"step","data":{...StepDto fields by alias...}}</c>
  /// — verified by <c>carestechs-agent-orchestrator/tests/test_cli_runs.py:246-256</c>.
  ///
  /// Conservative on edge cases: returns null rather than walking back to an older
  /// step whose nodeInputs is intact, to avoid surfacing a stale id during a transient state.
  /// </summary>
  public static string? LatestStepTaskId(IEnumerable<JsonElement> traceRecords)
  ```
- Iterate `traceRecords` in order. Track the latest `kind == "step"` record. After the loop, descend `rec.data.nodeInputs.taskId` (object → property `data` → property `nodeInputs` → property `taskId`); return the string if non-empty; otherwise `null`. Use the existing `TryGetProperty` + `ValueKind == JsonValueKind.String` pattern from `ParseAssignmentsFromTrace`.

### Step 2: Rewrite `ParseAssignmentsFromTrace`
**File:** `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs`
**Action:** Modify

- Replace the existing body (lines 46-64) with a version that:
  - Filters on `kind == "operator_signal"` (not `"signal"`).
  - For each accepted record, reads `rec.data` (object) and then `data.name` / `data.taskId` / `data.payload.assignee`.
  - Same last-write-wins semantics; same null/empty/non-string skipping.
- Update xmldoc to cite `_KIND_BY_TYPE` (orchestrator `service.py:73-78`) and the empirical evidence from `test_lifecycle_anthropic_mocked.py:239`.

### Step 3: Delete `LatestSignalTaskId`
**File:** `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs`
**Action:** Modify

- Remove the method. Confirm `grep -rn "LatestSignalTaskId" src/ tests/` returns zero hits (it's referenced only from `OrchestratorExecutorClient.cs:103` today — that call site is updated in Step 4).

### Step 4: Swap the fallback in `FetchStateAsync`
**File:** `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs`
**Action:** Modify

- Change `OrchestratorExecutorClient.cs:102-103`:
  ```csharp
  if (currentTaskId is null && status == "WaitingOnCheckpoint")
      currentTaskId = ExecutorStateProjection.LatestStepTaskId(traceRecords);
  ```
- Update the inline comment at `OrchestratorExecutorClient.cs:98-99` to reflect the new derivation: "One trace scan for assignments + currentTaskId (latest step's `data.nodeInputs.taskId`)."
- No other changes in this file.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs` | Modify | Add `LatestStepTaskId`; rewrite `ParseAssignmentsFromTrace` against `{kind, data}` shape; delete `LatestSignalTaskId`. |
| `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs` | Modify | Swap fallback call + update comment. |

## Edge Cases & Risks

- **`rec.data` is missing or wrong kind.** Skip the record (`continue` in the loop). All field accesses use `TryGetProperty` + `ValueKind` checks; matching the existing defensive style.
- **`rec.data.nodeInputs` is empty `{}`.** Returns `null` for `LatestStepTaskId` — correct, the step didn't carry a per-task discriminator.
- **`rec.data.nodeInputs.taskId` is empty string.** Treat as missing — return `null`.
- **Empty trace.** Both helpers return `null` / empty dict respectively. Caller handles `null` already (it propagates to `ExecutorFetchResponse.CurrentTaskId`).
- **Existing tests will fail after this task.** Expected and intentional — they assert against the old (wrong) flat shape. T-095 updates the harness and the existing fixtures; T-096 adds the regression coverage. **Do not run `dotnet test` in isolation after T-094; it WILL show failures until T-095 lands.** The acceptance criterion below specifies `dotnet build`, not `dotnet test`.
- **The `lastStep` field (`RunDetailDto.last_step`) is NOT under a `data` wrapper.** `OrchestratorExecutorClient.cs:84-91` reads `lastStep.nodeName` directly off `detail.lastStep` — that path is correct and stays unchanged. Only the trace-stream records are wrapped under `data`.

## Acceptance Verification

- [ ] `LatestStepTaskId` exists with the documented behavior + xmldoc citing the orchestrator-side tests.
- [ ] `ParseAssignmentsFromTrace` filters on `"operator_signal"` and reads `rec.data.*`.
- [ ] `LatestSignalTaskId` is deleted. `grep -rn "LatestSignalTaskId" src/ tests/` returns zero hits (note: test references will be removed in T-095).
- [ ] `OrchestratorExecutorClient.cs:102-103` calls `LatestStepTaskId`.
- [ ] `dotnet build` green. (Tests defer to T-095/T-096 — they will fail in isolation against this task alone.)
