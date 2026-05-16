# Implementation Plan: T-038 — WorkItems integration tests

## Task Reference
- **Task ID:** T-038 · **Type:** Testing · **Workflow:** standard · **Complexity:** L
- **Rationale:** FEAT-001 discipline plus FEAT-004's explicit ACs. Stream behavior is impossible to validate without a real upstream — the fake executor (T-035) is the workhorse.

## Overview
Four test files:
1. `WorkItemsEndpointsTests` — JSON endpoints, grant/deny + status codes.
2. `CheckpointSignalsEndpointsTests` — signal grant/deny + 400 outcome + 404 unknown checkpoint + 409 already-resolved + idempotency replay.
3. `WorkItemStreamTests` — SSE byte-for-byte, deny no-open, disconnect cleanup.
4. `FacadeAcceptanceTests` — the cross-cutting ACs (AC-1, AC-3, AC-6, AC-2 soft).

## Implementation Steps

### Step 1: Test project deps
**File:** `tests/DevHub.Modules.WorkItems.Tests/DevHub.Modules.WorkItems.Tests.csproj` · Modify
Add refs to `Workspace`, `Identity`, `Audit`, `ExecutorRegistry` (cross-module helpers + audit reads).

### Step 2: Helpers
**File:** `tests/DevHub.Modules.WorkItems.Tests/Helpers/WorkItemsTestHelpers.cs` · Create

- `LoginOperatorAsync` / `LoginFreshMemberAsync` (delegates to existing).
- `CreateProjectAsync(team, projectType="feature-delivery")`.
- `StartWorkItemAsync(operator, projectId, title="Test")` → returns `WorkItemDto`.
- `SignalAsync(client, projectId, workItemId, checkpointKey, outcome, idempotencyKey?)` → returns the HTTP response.
- `AuditEntriesForActionAsync` (delegates).

### Step 3: WorkItemsEndpointsTests
**File:** `tests/DevHub.Modules.WorkItems.Tests/WorkItemsEndpointsTests.cs` · Create

Factory: `WithFakeExecutor = true`. Tests:
1. `Start_as_operator_returns_201_and_audits_granted_with_marker` — assert audit row's `Details` contains the `executorCorrelationMarker`.
2. `Start_as_non_member_returns_403_no_outbound_call` — `fake.Calls.Total.Should().Be(0)`.
3. `Start_with_no_executor_binding_returns_409` — opt-out of seed binding for this test; expect 409 (handled by `IExecutorRouter.ResolveAsync == null`).
4. `Get_as_member_returns_dto_with_executor_state`.
5. `Get_as_non_member_returns_403_no_outbound_call`.
6. `List_paginated_returns_envelope_with_meta`.
7. `Cancel_as_operator_forwards_and_audits_granted`.
8. `Cancel_as_non_member_returns_403_no_outbound_call`.

### Step 4: CheckpointSignalsEndpointsTests
**File:** `tests/DevHub.Modules.WorkItems.Tests/CheckpointSignalsEndpointsTests.cs` · Create

The fake's scripted defaults: after `Start`, the work item is `WaitingOnCheckpoint(approve)`. Tests:
1. `Signal_with_correct_role_returns_200_and_audits` — operator signals `approve/approve`.
2. `Signal_as_non_member_returns_403_no_outbound_call`.
3. `Signal_with_outcome_not_in_contract_returns_400` — `outcome="banana"`; `fake.Calls.Signal == 0`.
4. `Signal_for_unknown_checkpoint_key_returns_404`.
5. `Signal_when_checkpoint_already_resolved_returns_409` — script the work item to `Completed` before signaling.
6. `Signal_with_repeated_idempotency_key_returns_original_response` — same key twice; second call: `fake.Calls.Signal == 1` (no second forward); response body matches first.
7. `Signal_history_returns_paginated_signals`.

### Step 5: WorkItemStreamTests
**File:** `tests/DevHub.Modules.WorkItems.Tests/WorkItemStreamTests.cs` · Create

Tests:
1. `Stream_as_non_member_returns_403_and_does_not_open_upstream` — assert `fake.Calls.OpenStream == 0`.
2. `Stream_as_member_yields_chunks_byte_for_byte` — fake scripts 3 chunks with 50ms delay; client reads with `StreamReader.ReadAsync`, records `Stopwatch.GetTimestamp()` per chunk; assert deltas ≥ 30ms (50ms minus tolerance).
3. `Stream_client_disconnect_closes_upstream_within_1s` — abort the client `HttpRequestMessage` mid-stream; poll `fake.OpenStreamConnections` (a counter the fake tracks); assert it returns to 0 within 1000ms.
4. `Stream_grants_one_audit_row_per_connection_not_per_chunk` — open, consume some bytes, close; assert exactly one Granted audit row for `workitem:stream`.

### Step 6: FacadeAcceptanceTests
**File:** `tests/DevHub.Modules.WorkItems.Tests/FacadeAcceptanceTests.cs` · Create

Tests pegged to ACs:
1. `AC1_deny_path_never_reaches_executor` — sweep every endpoint as a fresh non-member; assert `fake.Calls.Total == 0` after the suite runs.
2. `AC3_granted_audits_carry_executor_correlation_marker` — start + signal + cancel; assert each Granted audit's `Details["executorCorrelationMarker"]` is non-null and consistent across the three rows (start sets it; signal + cancel reuse it).
3. `AC6_signal_with_invalid_outcome_returns_400_before_forward` — `outcome="banana"`; `fake.Calls.Signal == 0`.
4. `AC2_devhub_round_trip_within_5x_direct` — soft: time `client.GetAsync($"/api/projects/{p}/work-items/{w}")` vs the fake's direct `/work-items/{marker}` from a fresh HttpClient; assert ratio < 5.

### Step 7: Existing tests sweep
The `TestRegistrySeeder` already seeds a `feature-delivery` executor + binding; T-035's `WithFakeExecutor` rewires the base URL to the fake host. Existing tests don't need changes — they don't exercise WorkItems endpoints.

## Files Affected
| File | Action |
|------|--------|
| `DevHub.Modules.WorkItems.Tests.csproj` | Modify (refs) |
| `Helpers/WorkItemsTestHelpers.cs` | Create |
| `WorkItemsEndpointsTests.cs` | Create |
| `CheckpointSignalsEndpointsTests.cs` | Create |
| `WorkItemStreamTests.cs` | Create |
| `FacadeAcceptanceTests.cs` | Create |

## Edge Cases & Risks
- **Chunk-delta tolerance** — CI clocks are noisy. 30ms floor on a 50ms scripted delay leaves room. If still flaky, raise the scripted delay to 100ms.
- **Stream disconnect detection** — the fake host needs an `int OpenStreamConnections` counter incremented in the SSE endpoint and decremented in its `finally`. Without it, the disconnect test is observational only.
- **JWT for SSE in tests** — the test client uses `Authorization: Bearer` (works for `HttpClient`). The `?access_token=` workaround is for the browser; integration tests don't exercise it. We add a dedicated controller test that hits the stream with `?access_token=` and a missing `Authorization` header.

## Acceptance Verification
- [ ] `dotnet test` is green; new WorkItems test count ≥ 20.
- [ ] Existing 71 backend tests + 114 SPA tests stay green.
- [ ] AC checklist:
  - AC-1 verified by Step 6.1.
  - AC-2 verified by Step 6.4 (soft).
  - AC-3 verified by Step 6.2.
  - AC-4 verified by Step 5.2.
  - AC-5 verified by Step 3.7+8 (cancel role-gated).
  - AC-6 verified by Step 6.3.
