# Implementation Plan: T-073 — Integration tests for per-task lifecycle

## Task Reference
- **Task ID:** T-073 · **Type:** Testing · **Workflow:** standard · **Complexity:** M
- **Rationale:** Closes the AC loop on per-task pending identity, loop-back semantics, signal forwarding, validation, and audit.

## Overview
Two new test classes — one on the Notifications side (per-task pending identity + loop-back + backward compatibility), one on the WorkItems side (signal forward + assignee validation + audit). May need a small FakeExecutor harness extension for multi-task scripting.

## Implementation Steps

### Step 1: Extend FakeExecutor for multi-task scripting (if needed)
**File:** `tests/DevHub.TestHarness/FakeExecutor/FakeExecutorHost.cs` · Modify

The `Scripted` profile currently sets `StartStatus`, `StartCheckpointKey`, `FetchStatus`, etc. — single values. For per-task tests, we need to mutate the "current task id" between calls. Add a single mutable field:

```csharp
public string? CurrentTaskId { get; set; }
```

And include it in the response bodies — start, fetch, signal each emit `currentTaskId = marker.Owner.Scripted.CurrentTaskId`. Tests mutate the value via the existing `Scripted` reference.

(If the harness's `Scripted` is already a class with public setters, this is a one-line addition. If it's a record / read-only, a small refactor.)

### Step 2: Notifications-side test class
**File:** `tests/DevHub.Modules.Notifications.Tests/Acceptance/PerTaskPendingActionTests.cs` · Create

```csharp
[Collection("postgres")]
public class PerTaskPendingActionTests : IAsyncLifetime
{
    // Standard fixture setup — see CodeSourceForwardTests for the pattern.

    [Fact]
    public async Task Per_task_contract_raises_rows_keyed_by_taskId()
    {
        // Register an executor with assignment-confirmed perTask=true.
        // Seed a work item; advance to checkpoint=assignment-confirmed with CurrentTaskId=T-001.
        // Reconcile.
        // Assert: PendingActionSignal rows exist with TaskId="T-001".
    }

    [Fact]
    public async Task Loop_back_T_001_closed_then_T_002_opens_each_distinct()
    {
        // Setup: same as above with T-001.
        // Reconcile → row for T-001 exists.
        // Advance executor: CurrentTaskId moves to T-002 (still on assignment-confirmed).
        // Reconcile.
        // Assert:
        //   - T-001 row has DismissedAt set (stale-rows pass dismissed it because TaskId differs).
        //   - T-002 row exists with DismissedAt=null.
        //   - T-001 row is NOT re-opened.
    }

    [Fact]
    public async Task Per_task_false_contract_keeps_legacy_keying()
    {
        // Same executor but assignment-confirmed contract has perTask=false.
        // Even with CurrentTaskId="T-001" on the work item, the raised row has TaskId=null.
        // Existing test suite's invariants apply.
    }
}
```

### Step 3: WorkItems-side test class
**File:** `tests/DevHub.Modules.WorkItems.Tests/Acceptance/AssignmentSignalTests.cs` · Create

```csharp
[Collection("postgres")]
public class AssignmentSignalTests : IAsyncLifetime
{
    [Fact]
    public async Task Signal_with_taskId_and_assignee_forwards_both_to_executor()
    {
        // Setup: project + work item parked on assignment-confirmed with CurrentTaskId=T-001.
        // POST /signal { outcome: "confirmed", payload: { assignee: "Alice" }, taskId: "T-001" }.
        // Assert: FakeExecutor recorded body contains "taskId":"T-001" + "payload":{"assignee":"Alice"}.
    }

    [Fact]
    public async Task Signal_assignment_confirmed_without_assignee_returns_400_no_executor_call()
    {
        // POST /signal { outcome: "confirmed", payload: {} }.
        // Assert: 400 with problem-detail errors.payload.assignee populated.
        // Fake.Total unchanged from pre-call.
    }

    [Fact]
    public async Task Signal_other_checkpoint_without_assignee_still_succeeds()
    {
        // assignment-confirmed guard is checkpoint-specific. A regular "approve" with empty payload works as today.
    }

    [Fact]
    public async Task Audit_signal_row_carries_taskId_and_assignee_in_details()
    {
        // After AC-1's successful submit, query the audit table.
        // Assert: workitem:signal row's DetailsJson contains both keys with the right values.
    }
}
```

### Step 4: Run the suite
**Bash:**

```bash
dotnet test
```

All existing tests stay green; +7 new tests (3 + 4).

## Files Affected
| File | Action |
|------|--------|
| `tests/DevHub.Modules.Notifications.Tests/Acceptance/PerTaskPendingActionTests.cs` | Create |
| `tests/DevHub.Modules.WorkItems.Tests/Acceptance/AssignmentSignalTests.cs` | Create |
| `tests/DevHub.TestHarness/FakeExecutor/FakeExecutorHost.cs` | Modify (if multi-task scripting not already supported) |

## Edge Cases & Risks
- **Reconciler timing.** The reconciler runs after every transition in the production path. Tests need to trigger reconciliation explicitly (call `RecomputeForWorkItemAsync(...)` from the fixture) — match the pattern in existing FEAT-005 notifications tests.
- **EF null-equality.** T-067 plan calls out `p.TaskId == currentTaskId` with both nulls. One of the per-task-false tests above should exercise that path (both null on both sides), confirming the row matches as expected.
- **Audit `DetailsJson` is raw JSON.** Use `.Contains("taskId")` / `.Contains("Alice")` style assertions like the FEAT-008 tests did.

## Acceptance Verification
- [ ] 3 Notifications-side tests cover per-task identity + loop-back + backward compat.
- [ ] 4 WorkItems-side tests cover forward shape + assignee validation + audit.
- [ ] Existing 182 backend tests stay green.
- [ ] FakeExecutor harness change (if any) doesn't break the broader test suite.
