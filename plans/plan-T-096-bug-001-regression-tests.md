# Implementation Plan: T-096 — BUG-001 regression tests for both projections + status flip

## Task Reference
- **Task ID:** T-096
- **Type:** Testing
- **Workflow:** standard
- **Complexity:** S
- **Dependencies:** T-094 + T-095 (must ship in the same PR; this task's tests are written against the realigned production code and harness).
- **Rationale:** BUG-001 §10 requires regression tests on the FakeOrchestrator harness for both projections (`currentTaskId` AND `assignments`). This task lands the unit + integration coverage, then flips BUG-001 to `Resolved`.

## Overview
Two new test files:
- `tests/DevHub.Modules.WorkItems.Tests/ExecutorStateProjectionTests.cs` — unit tests for `LatestStepTaskId` and the rewritten `ParseAssignmentsFromTrace`, against the realigned `{kind, data}` shape.
- `tests/DevHub.Modules.WorkItems.Tests/OrchestratorTraceProjectionsTests.cs` — integration tests via `FakeOrchestrator` for the BUG-001 §2 defining scenario (currentTaskId) AND for the assignments projection.

After tests are green, flip BUG-001 §1 Status to `Resolved` and append the regression-test pointer to §10.

## Implementation Steps

### Step 1: Unit tests for `LatestStepTaskId`
**File:** `tests/DevHub.Modules.WorkItems.Tests/ExecutorStateProjectionTests.cs`
**Action:** Create

- Pure unit tests — no Postgres, no HTTP. Build trace records as JSON literals via `JsonDocument.Parse(...)`.
- Helper:
  ```csharp
  private static IReadOnlyList<JsonElement> Trace(string ndjson) =>
      ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
  ```
- Test cases:
  1. **Empty trace** → `LatestStepTaskId(Array.Empty<JsonElement>())` returns `null`.
  2. **Only operator_signal records** → returns `null`. Sample line: `{"kind":"operator_signal","data":{"name":"assignment-confirmed","taskId":"T-1","payload":{}}}`.
  3. **Multiple step records with different task ids** → returns the last one. Lines: `{"kind":"step","data":{"nodeName":"confirm_assignment","nodeInputs":{"taskId":"T-1"}}}` then `…{"taskId":"T-2"}}` — assert `"T-2"`.
  4. **Latest step lacks `nodeInputs.taskId`** → returns `null` (does not walk back). Lines: `…{"taskId":"T-1"}` then `…"nodeInputs":{}}` — assert `null`.
  5. **Empty-string `taskId`** → returns `null`.
  6. **`data` field missing** → record skipped; helper returns `null` (defensive).

### Step 2: Unit tests for `ParseAssignmentsFromTrace`
**File:** `tests/DevHub.Modules.WorkItems.Tests/ExecutorStateProjectionTests.cs`
**Action:** Modify (same file as Step 1)

- Test cases:
  1. **Empty trace** → empty dict.
  2. **Single `assignment-confirmed` `operator_signal`** → `{ "T-1" → "alice" }`. Sample: `{"kind":"operator_signal","data":{"name":"assignment-confirmed","taskId":"T-1","payload":{"assignee":"alice"}}}`.
  3. **Two signals same task, different assignees** → last-write-wins (`"T-1" → "bob"`).
  4. **Signal with different name (`tasks-confirmed`)** → excluded; dict empty.
  5. **Signal with missing `taskId`** → skipped.
  6. **Signal with non-string assignee** → skipped.
  7. **Signal with empty assignee** → skipped.
  8. **`signal` (wrong, old kind name) records** → skipped; dict empty. Regression guard against re-introducing the flat-shape anti-pattern.

### Step 3: Integration test — currentTaskId scenario (BUG-001 §2)
**File:** `tests/DevHub.Modules.WorkItems.Tests/OrchestratorTraceProjectionsTests.cs`
**Action:** Create

- Mirror the setup pattern from `OrchestratorExecutorClientTests.cs` — use `DevHubApiFactory` configured with the FakeOrchestrator.
- Single test `Get_work_item_currentTaskId_reflects_latest_step_nodeInputs_taskId`:
  1. Start a work item against the orchestrator-protocol executor.
  2. Set `Scripted.CurrentRunStatus = "awaiting_signal"`, `Scripted.LastStep = new LastStepDto(..., NodeName: "confirm_assignment", ...)`.
  3. Append `TraceRecord.Step("confirm_assignment", taskId: "T-1")` to `Scripted.TraceRecords`. Do NOT append any signal records.
  4. `GET /api/projects/{pid}/work-items/{wid}`. Assert `data.currentStatus == "WaitingOnCheckpoint"`, `data.currentCheckpointKey == "assignment-confirmed"`, `data.currentTaskId == "T-1"`.
  5. Append `TraceRecord.Step("confirm_assignment", taskId: "T-2")`. Re-fetch. Assert `data.currentTaskId == "T-2"`.

### Step 4: Integration test — assignments projection
**File:** `tests/DevHub.Modules.WorkItems.Tests/OrchestratorTraceProjectionsTests.cs`
**Action:** Modify (same file as Step 3)

- Test `Get_work_item_assignments_reflects_operator_signal_in_trace`:
  1. Start a work item, set status to `"awaiting_signal"`.
  2. Append `TraceRecord.OperatorSignal("assignment-confirmed", taskId: "T-1", payload: new { assignee = "alice" })`.
  3. GET work item. Assert `data.executorState.assignments["T-1"] == "alice"`.
  4. Append `TraceRecord.OperatorSignal("assignment-confirmed", taskId: "T-1", payload: new { assignee = "bob" })`. GET. Assert `data.executorState.assignments["T-1"] == "bob"` (last-write-wins).
  5. Append `TraceRecord.OperatorSignal("tasks-confirmed", taskId: "T-1", payload: new { items = new[] { "T-1" } })`. GET. Assert `tasks-confirmed` does NOT appear in `assignments` (only `assignment-confirmed` is captured).

### Step 5: Flip BUG-001 status + pointer
**File:** `docs/work-items/BUG-001-orchestrator-current-task-id.md`
**Action:** Modify

- §1 Identity: set **Status** to `Resolved`.
- Append to §10 Notes:
  > **Regression tests added (T-096):**
  > - Unit: `tests/DevHub.Modules.WorkItems.Tests/ExecutorStateProjectionTests.cs` covers `LatestStepTaskId` and `ParseAssignmentsFromTrace` against the realigned `{kind, data}` shape. Includes a guard test that the old `kind == "signal"` (flat-shape) anti-pattern would no longer produce assignments.
  > - Integration: `tests/DevHub.Modules.WorkItems.Tests/OrchestratorTraceProjectionsTests.cs` runs the BUG-001 §2 scenario through DevHub's public API and asserts both `currentTaskId` and `executorState.assignments` projections.

### Step 6: Full-suite verification
**File:** N/A
**Action:** Verify

- `dotnet test --nologo`. Expect 177 + 14 new tests (~6 unit + ~2 integration, depending on parameterization) all green.
- Manual sanity check (one-time, do not commit): temporarily revert one of T-094's projection rewrites (e.g. change `"operator_signal"` back to `"signal"`). Confirm the corresponding new test fails. Restore. This is the "would have caught the bug" check.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `tests/DevHub.Modules.WorkItems.Tests/ExecutorStateProjectionTests.cs` | Create | Unit tests for both projections against the realigned shape. |
| `tests/DevHub.Modules.WorkItems.Tests/OrchestratorTraceProjectionsTests.cs` | Create | Integration tests via FakeOrchestrator for currentTaskId + assignments. |
| `docs/work-items/BUG-001-orchestrator-current-task-id.md` | Modify | Status → Resolved; append regression-test pointers to §10. |

## Edge Cases & Risks

- **Test fixture for the integration tests assumes a per-task checkpoint contract.** The auto-seed in `DevHubApiFactory` may register a non-per-task contract by default. If so, register a `confirm_assignment` contract with `perTask = true` as part of test setup — copy the pattern from FEAT-009 / T-088 tests.
- **JSON-element parsing in unit tests.** `JsonDocument` is disposable. Either use `using` carefully or `Clone()` the element to keep it valid past the dispose (see the `Trace` helper above).
- **Don't depend on FakeOrchestrator implementation details.** Tests assert on DevHub's public API response only (`GET /api/projects/{pid}/work-items/{wid}` → JSON). If a future change to the harness affects the on-the-wire shape, those changes belong in T-095, not here.
- **Step 6 sanity-check is a one-time manual action.** Do not commit a permanent test that depends on a buggy state.

## Acceptance Verification

- [ ] 6+ new unit tests pass under `ExecutorStateProjectionTests`.
- [ ] 2 new integration tests pass under `OrchestratorTraceProjectionsTests`.
- [ ] `dotnet test` is green end-to-end.
- [ ] BUG-001 §1 Status is `Resolved`.
- [ ] BUG-001 §10 has pointers to both new test files.
- [ ] One-time manual sanity check (Step 6) confirms a reverted projection fails the relevant new test.
