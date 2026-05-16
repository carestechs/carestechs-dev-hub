# Implementation Plan: T-045 — Notifications integration tests

## Task Reference
- **Task ID:** T-045 · **Type:** Testing · **Workflow:** standard · **Complexity:** L
- **Rationale:** AC-1 / AC-2 / AC-3 / AC-5 are external promises that need explicit assertions. The reconciler's logic (who-gets-a-row, dismiss-on-resolve, backfill) is the most failure-prone surface in FEAT-005.

## Overview
Four test files: reconciler unit-ish (real Postgres, no HTTP), endpoint integration, stream integration, and a Workspace-side backfill test.

## Implementation Steps

### Step 1: Test project deps
**File:** `tests/DevHub.Modules.Notifications.Tests/DevHub.Modules.Notifications.Tests.csproj` · Modify
Add refs to `Workspace`, `Identity`, `Audit`, `WorkItems`, `ExecutorRegistry` (cross-module helpers + audit reads).

### Step 2: Helpers
**File:** `tests/DevHub.Modules.Notifications.Tests/Helpers/NotificationsTestHelpers.cs` · Create

- `LoginOperatorAsync` / `LoginFreshMemberAsync` (delegates to existing).
- `SeedApproveContractAsync(client)` — registers a `feature-delivery-v1` `approve` contract with `requiredRoleKey="reviewer"`.
- `CreateProjectAsync`, `AddMembershipAsync(client, projectId, memberId, roles)`.
- `StartWorkItemAsync(client, projectId)` → DTO.
- `ListPendingAsync(client)` → `PendingActionDto[]`.
- `OpenStreamAsync(client, ct)` — opens the SSE stream and returns an async-iterator of parsed events with a 3s per-event timeout.

### Step 3: PendingActionReconcilerTests
**File:** `tests/DevHub.Modules.Notifications.Tests/PendingActionReconcilerTests.cs` · Create

Run with `[Collection("postgres")]`, factory `UseFakeExecutor=true`. Each test:
1. Seeds operator, project, work item with `WaitingOnCheckpoint(approve)` (via fake script).
2. Calls the reconciler directly through DI scope.
3. Asserts the resulting `PendingActionSignal` rows via direct DbContext query.

Tests:
- `WaitingOnCheckpoint_upserts_rows_for_members_with_required_role`
- `Operator_gets_row_without_project_membership`
- `Terminal_status_dismisses_all_rows_for_workitem` (script Signal → Completed, then call reconciler)
- `Status_change_to_different_checkpoint_dismisses_old_and_raises_new`
- `Member_loses_required_role_dismisses_their_row`
- `Idempotent_second_call_writes_zero_rows`
- `Recompute_for_member_in_project_backfills_pending_rows`

### Step 4: NotificationsEndpointsTests
**File:** `tests/DevHub.Modules.Notifications.Tests/NotificationsEndpointsTests.cs` · Create

Tests:
- `Pending_as_operator_returns_caller_scoped_rows`
- `Pending_does_not_leak_rows_from_other_members`
- `Pending_after_signal_resolution_excludes_dismissed`
- `Pending_anonymous_returns_401`

### Step 5: NotificationStreamTests
**File:** `tests/DevHub.Modules.Notifications.Tests/NotificationStreamTests.cs` · Create

Tests covering AC-1 / AC-2 / AC-5:
- **AC-1**: open stream as the seeded operator → start a work item (which becomes `WaitingOnCheckpoint(approve)`) → observe a `"raised"` event on the stream within 2s.
- **AC-2**: open stream → signal `approve` → observe a `"dismissed"` event for the just-resolved row within 2s.
- **AC-5**: open stream A → disconnect → open stream B → trigger a transition → exactly one `"raised"` event arrives on B and zero on A (A's reader is already disposed).

Pattern:
```csharp
using var streamCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
using var req = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/stream");
using var resp = await _operator.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, streamCts.Token);
await using var stream = await resp.Content.ReadAsStreamAsync(streamCts.Token);
using var reader = new StreamReader(stream);
// ...trigger transition in another scope...
var line = await reader.ReadLineAsync(streamCts.Token);
// SSE lines look like "data: {json}"
```

Helper `AwaitFirstEventAsync(reader, predicate, ct)` reads lines until a `data:` line matching the predicate is found or the CTS fires.

### Step 6: MembershipBackfillTests
**File:** `tests/DevHub.Modules.Workspace.Tests/MembershipBackfillTests.cs` · Create

Setup: operator creates project + starts work item (WaitingOnCheckpoint(approve)). Alice is a fresh member NOT on the project — no rows for her. Operator adds Alice with `reviewer` role. After the add, `GET /api/notifications/pending` as Alice returns the entry.

### Step 7: Existing-test sweep
The reconciler is called by every WorkItems mutation. Existing T-038 tests should still pass because:
- The fake's default scripted state for start is `WaitingOnCheckpoint(approve)`. Without an `approve` contract registered, the reconciler short-circuits (`contract is null → no-op`).
- Tests that explicitly register the `approve` contract (CheckpointSignalsEndpointsTests, FacadeAcceptanceTests) WILL now also write `PendingActionSignal` rows — verify those tests still pass.

If any existing audit-count assertions get tripped by the reconciler, document the +1 row and adjust the assertion.

## Files Affected
| File | Action |
|------|--------|
| `Notifications.Tests/DevHub.Modules.Notifications.Tests.csproj` | Modify (refs) |
| `Helpers/NotificationsTestHelpers.cs` | Create |
| `PendingActionReconcilerTests.cs` | Create |
| `NotificationsEndpointsTests.cs` | Create |
| `NotificationStreamTests.cs` | Create |
| `Workspace.Tests/MembershipBackfillTests.cs` | Create |

## Edge Cases & Risks
- **CI clock noise on AC-1 (≤2s).** Per-event timeout = 3s in tests is well over the AC threshold; cycle should be tens of milliseconds on the loopback fake.
- **AC-5 disconnect timing.** The registry's `IAsyncDisposable` cleanup must complete before stream B opens. Awaiting the disposal explicitly in the test makes this reliable.
- **Long-lived SSE in tests.** `HttpClient.Timeout` is global; opening the stream via `HttpCompletionOption.ResponseHeadersRead` + a per-request `CancellationTokenSource` is the workaround (same shape as T-038's stream tests).

## Acceptance Verification
- [ ] `dotnet test` for Notifications.Tests is green with ≥12 new tests.
- [ ] Existing 94 backend tests stay green (after the sweep in Step 7).
- [ ] AC checklist:
  - AC-1 verified by Step 5 #1.
  - AC-2 verified by Step 5 #2.
  - AC-3 isn't a test (it's a frontend contract — covered by T-046's spec).
  - AC-5 verified by Step 5 #3.
