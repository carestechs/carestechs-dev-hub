# Implementation Plan: T-067 — `PendingActionReconciler` per-task identity + DTO/event extension

## Task Reference
- **Task ID:** T-067 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-2, AC-3, AC-8. The core data-flow change.

## Overview
Reconciler keys by `(MemberId, WorkItemId, CheckpointKey, TaskId)` when the active contract has `perTask=true`. Otherwise behavior is byte-for-byte today. `PendingActionEvent` + `PendingActionDto` carry nullable `taskId`.

## Implementation Steps

### Step 1: Extend `PendingActionEvent`
**File:** `src/DevHub.Contracts/Notifications/PendingActionEvent.cs` · Modify

Add `string? TaskId` to the record. Default `null` if positional — every emit site needs to pass it.

### Step 2: Extend `PendingActionDto`
**File:** `src/DevHub.Modules.Notifications/DTOs/PendingActionDto.cs` · Modify

Same: add `string? TaskId` at the end.

### Step 3: Rewrite the reconciler's keying logic
**File:** `src/DevHub.Modules.Notifications/Services/PendingActionReconciler.cs` · Modify

```csharp
// Inside RecomputeForWorkItemAsync, after fetching wi + contract:
var perTask = contract.PerTask;
var currentTaskId = perTask ? wi.CurrentTaskId : null;

// Replace the existing existingForActiveKey query:
var existingForActiveKey = await db.PendingActionSignals
    .Where(p => p.WorkItemId == workItemId
                && p.CheckpointKey == wi.CurrentCheckpointKey
                && p.TaskId == currentTaskId   // null == null in EF query land
                && p.DismissedAt == null)
    .ToListAsync(ct);
```

Update the stale-rows query similarly to be discriminator-aware:

```csharp
var staleForOtherKeys = await db.PendingActionSignals
    .Where(p => p.WorkItemId == workItemId
                && (p.CheckpointKey != wi.CurrentCheckpointKey
                    || p.TaskId != currentTaskId)   // different task = different "active row"
                && p.DismissedAt == null)
    .ToListAsync(ct);
```

When creating new rows:

```csharp
db.PendingActionSignals.Add(new PendingActionSignal
{
    MemberId = memberId,
    ProjectId = wi.ProjectId,
    WorkItemId = workItemId,
    CheckpointKey = wi.CurrentCheckpointKey,
    TaskId = currentTaskId,
});
```

And every `PendingActionEvent` emit gains the `taskId` argument:

```csharp
pending.Add(new PendingActionEvent("raised", memberId, wi.ProjectId, workItemId,
    wi.CurrentCheckpointKey, currentTaskId, now));
```

For dismissed events, use `row.TaskId` (the dismissed row's own task id).

### Step 4: Update `NotificationsQueryService` projection
**File:** `src/DevHub.Modules.Notifications/Services/NotificationsQueryService.cs` · Modify

The DTO projection at line 35 gains `row.TaskId`:

```csharp
results.Add(new PendingActionDto(
    row.ProjectId, row.ProjectSlug,
    row.WorkItemId, row.WorkItemTitle,
    row.CheckpointKey, row.CheckpointDisplayName,
    row.RaisedAt,
    row.TaskId));
```

### Step 5: SSE event serialization
**File:** wherever `PendingActionEvent` is serialized for the SSE pass-through · Verify

Most likely System.Text.Json with `JsonPropertyName` attributes already in place on the record. Confirm `taskId` appears on the wire when non-null and is omitted (or sent as `null`) when null — match the existing field-style for the event.

### Step 6: Update `docs/api-spec.md`
**File:** `docs/api-spec.md` · Modify

`PendingActionDto` and the SSE event sections gain `taskId`. Changelog:

```
| 2026-05-17 (FEAT-009 / T-067) | PendingActionDto + PendingActionEvent gained optional taskId. Reconciler keys per-task pending rows when CheckpointContract.PerTask is true; otherwise behavior is unchanged. |
```

### Step 7: Run the suite
**Bash:**

```bash
dotnet test
```

Notifications acceptance tests (12) should pass — none rely on the absence of `taskId`. If any positional `PendingActionDto` / `PendingActionEvent` construction breaks, update those sites.

## Files Affected
| File | Action |
|------|--------|
| `src/DevHub.Contracts/Notifications/PendingActionEvent.cs` | Modify |
| `src/DevHub.Modules.Notifications/DTOs/PendingActionDto.cs` | Modify |
| `src/DevHub.Modules.Notifications/Services/PendingActionReconciler.cs` | Modify |
| `src/DevHub.Modules.Notifications/Services/NotificationsQueryService.cs` | Modify |
| `docs/api-spec.md` | Modify |

## Edge Cases & Risks
- **Per-task contract with `wi.CurrentTaskId == null`.** Shouldn't happen if the executor is well-behaved — but defensive: if `perTask=true` and the task id is missing, log a warning and treat as `null` (row keyed without a task discriminator). The COALESCE-with-'<root>' constraint from T-064 keeps uniqueness intact.
- **Loop-back semantics.** When `wi.CurrentTaskId` changes from `T-001` to `T-002` (with the same checkpoint key), the T-001 row falls into the stale-rows query and gets dismissed; new rows are raised for `T-002`. The discriminator-aware query handles this naturally.
- **EF query `p.TaskId == currentTaskId` with both null.** EF Core 10 maps this to `IS NOT DISTINCT FROM` (or equivalent) — verify in the generated SQL. If it falls back to `=`, two nulls won't match and rows leak. Add a unit-or-integration test that exercises the null-on-both-sides path.

## Acceptance Verification
- [ ] PendingActionEvent + PendingActionDto carry `taskId`.
- [ ] `perTask=true` contract: rows keyed per task; loop-back works.
- [ ] `perTask=false` contract: behavior identical to today.
- [ ] `dotnet test` 182/182 still green.
