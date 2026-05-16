# Implementation Plan: T-036 — WorkItems services + controllers (JSON façade)

## Task Reference
- **Task ID:** T-036 · **Type:** Backend · **Workflow:** standard · **Complexity:** XL
- **Rationale:** This is what DevHub exists for. AC-1/3/5/6 all land here. The stream endpoint splits out to T-037 because its lifecycle is different (no MVC formatters, manual byte copy).

## Overview
Five JSON endpoints under `/api/projects/{projectId}/work-items[*]`. Pattern per action:
1. Resolve the contract (for signal/cancel) via `IExecutorRouter.GetCheckpointContractAsync(executorId, key)`.
2. Authorize `(member, role, project, action)` via `IProjectAuthorizationService` — `requiredRoleKey` comes from the contract.
3. Open a `WorkItemsDbContext` transaction.
4. Validate (outcome in `allowedOutcomes`, work item exists, idempotency lookup).
5. Insert `CheckpointSignal` BEFORE forward (so we have evidence even on failure).
6. Forward via `IExecutorHttpClient`.
7. On success update status cache + `executor_response_status`; on failure raise `ExecutorFailureException` (mapped 502 by T-035's middleware).
8. Audit `Granted` (or `Failed` for 502) with `executorCorrelationMarker` + `correlationId` (when present).

## Implementation Steps

### Step 1: DTOs
**File:** `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs` · Create

```csharp
public sealed record WorkItemSummaryDto(
    Guid Id, Guid ProjectId, string Title, string CurrentStatus,
    string? CurrentCheckpointKey, ExecutorRefDto Executor,
    string ExecutorCorrelationMarker, DateTimeOffset CreatedAt, MemberRefDto CreatedBy);

public sealed record WorkItemDto(
    Guid Id, Guid ProjectId, string Title, string CurrentStatus,
    string? CurrentCheckpointKey, ExecutorRefDto Executor,
    string ExecutorCorrelationMarker, DateTimeOffset CreatedAt, MemberRefDto CreatedBy,
    JsonElement ExecutorState, IReadOnlyList<CheckpointSignalDto> Signals);

public sealed record ExecutorRefDto(Guid Id, string Key, string DisplayName);
public sealed record MemberRefDto(Guid Id, string DisplayName);

public sealed record CheckpointSignalDto(
    Guid Id, string CheckpointKey, string Outcome, MemberRefDto SignaledBy,
    DateTimeOffset SignaledAt, int? ExecutorResponseStatus, JsonElement? Payload);

public sealed class StartWorkItemRequest
{
    [Required, MaxLength(255)] public string Title { get; init; } = "";
    public JsonElement Input { get; init; }
}
public sealed class SignalRequest
{
    [Required, MaxLength(60)] public string Outcome { get; init; } = "";
    public JsonElement? Payload { get; init; }
}
```

### Step 2: WorkItemsService (start, list, get, cancel)
**File:** `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` · Create

`StartAsync(projectId, request, currentMemberId, ct)`:
1. `var descriptor = await router.ResolveAsync(projectId, ct) ?? throw new ConflictException("No executor bound for this project.");`
2. `var startContract = descriptor.Contracts.FirstOrDefault(c => c.CheckpointKey == "start");`
3. `var requiredRole = startContract?.RequiredRoleKey ?? "operator";`
4. `await authz.EnsureAuthorizedAsync(currentMemberId, projectId, "workitem:start", requiredRole, ct);`
5. Begin tx. `var marker = Guid.NewGuid().ToString("N");`
6. `var startResp = await executorClient.StartAsync(descriptor, marker, request.Input, ct);` — may throw `ExecutorFailureException` (Failed audit written by ProblemDetails middleware? No, write here too so we capture inside the tx).
7. Insert `WorkItem` with `executor_correlation_marker = marker`, status from the response.
8. Audit `Granted` with `Details = { executorKey, executorCorrelationMarker = marker }`. Save + commit.
9. Return `WorkItemDto` (use the fresh fetch state from the response — no second call).

`GetAsync(projectId, workItemId, currentMemberId, ct)`: authorize project:any → fetch row → call `FetchStateAsync` → overwrite status cache → return DTO. Failure: 502, with `Failed` audit.

`ListAsync(projectId, page, statusFilter, waitingOnMe, currentMemberId, ct)`: project:any → DB-only read of the cache → paged DTO list. Filters apply server-side.

`CancelAsync(projectId, workItemId, currentMemberId, ct)`:
1. Resolve work item; resolve descriptor; resolve cancel contract (`checkpointKey == "cancel"`).
2. `requiredRole = cancelContract?.RequiredRoleKey ?? "operator"`.
3. Authorize. Begin tx. Forward `CancelAsync`. Audit Granted. Save.

### Step 3: CheckpointSignalsService
**File:** `src/DevHub.Modules.WorkItems/Services/CheckpointSignalsService.cs` · Create

`SignalAsync(projectId, workItemId, checkpointKey, request, idempotencyKey, currentMemberId, ct)`:
1. Resolve work item (must exist; 404 if not).
2. Resolve descriptor + contract for this `checkpointKey` (404 if contract missing).
3. Validate `request.Outcome ∈ contract.AllowedOutcomes` (400 if not — BEFORE auth so we don't audit on validation noise; actually the spec says authorize first, but 400 prevents wasted audit. Pick one: **authorize FIRST**, so an unauthorized caller can't probe `allowedOutcomes`. Add the validation right after.).
4. Authorize against `contract.RequiredRoleKey`.
5. Begin tx. If `idempotencyKey` is non-null, look up `(workItemId, idempotencyKey)` — if found, return the previous response (no second forward).
6. Validate `request.Outcome ∈ contract.AllowedOutcomes` → 400 if not.
7. Validate `WorkItem.CurrentStatus == "WaitingOnCheckpoint"` and `CurrentCheckpointKey == checkpointKey` → 409 if not.
8. Insert `CheckpointSignal` with `IdempotencyKey`, marker copied from the work item, `executor_response_status = null`.
9. Forward `SignalAsync`. On success: update the signal row with `executor_response_status` + `executor_response_at`; update the work item cache. On failure: raise `ExecutorFailureException` (signal row stays with null response — see FEAT-006).
10. Audit Granted with `Details = { executorCorrelationMarker, outcome, idempotencyKey, executorResponseStatus }`.

`ListSignalsAsync(projectId, workItemId, page, ct)`: project:any → DB read → paged DTO list.

### Step 4: Controllers
**Files (Create):**
- `src/DevHub.Modules.WorkItems/Controllers/WorkItemsController.cs`
- `src/DevHub.Modules.WorkItems/Controllers/CheckpointSignalsController.cs`

Thin: parse → call service → return envelope. `WorkItemsController` routes: `[Route("api/projects/{projectId:guid}/work-items")]` with `GET ""`, `POST ""`, `GET "{id:guid}"`, `POST "{id:guid}/cancel"`. `CheckpointSignalsController`: `[Route("api/projects/{projectId:guid}/work-items/{workItemId:guid}")]` with `GET "checkpoints/{key}"`, `POST "checkpoints/{key}/signal"`, `GET "signals"`. The signal action reads `Request.Headers["Idempotency-Key"]` and passes it to the service.

### Step 5: DI
**File:** `src/DevHub.Modules.WorkItems/WorkItemsModuleExtensions.cs` · Modify
```csharp
services.AddScoped<IWorkItemsService, WorkItemsService>();
services.AddScoped<ICheckpointSignalsService, CheckpointSignalsService>();
```

## Files Affected
| File | Action |
|------|--------|
| `WorkItems/DTOs/WorkItemDtos.cs` | Create |
| `WorkItems/Services/WorkItemsService.cs`, `CheckpointSignalsService.cs` | Create |
| `WorkItems/Controllers/{WorkItems,CheckpointSignals}Controller.cs` | Create |
| `WorkItems/WorkItemsModuleExtensions.cs` | Modify |

## Edge Cases & Risks
- **Audit on `ExecutorFailureException`** — the ProblemDetails middleware translates the exception to 502, but the audit row needs to land *inside the transaction* before the exception propagates. Approach: wrap the forward call in `try { ... } catch (ExecutorFailureException ex) { write Failed audit; await save; await commit; throw; }`. The `Failed` row preserves evidence of the attempt; the controller still returns 502 to the caller.
- **Idempotency cleanup** — there's no TTL job in v1. The unique partial index allows the row to live forever; document that the idempotency-key window is "until row pruned by FEAT-006's archive task" rather than 24h. (Update the task wording to match — the 24h is aspirational.)
- **Cancel of a terminal work item** — 409 from us (status check), don't forward. Audit Denied with `reason = "work item is in terminal status"`.
- **`status` filter parsing** — the value is opaque (executor-defined). Accept comma-separated, push through to `WHERE current_status IN (...)`.

## Acceptance Verification
- [ ] `dotnet build` clean.
- [ ] Manual smoke: with the fake executor running, hit each endpoint via curl with the operator JWT.
- [ ] Existing tests stay green; T-038 lands the grant/deny + idempotency + ExecutorFailure tests.
