# Implementation Plan: T-065 — Executor responses + `WorkItem.CurrentTaskId` thread-through

## Task Reference
- **Task ID:** T-065 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-1, AC-2. The reconciler needs to know "what task is this work item on right now"; the executor is authoritative.

## Overview
Three executor-response records gain optional `CurrentTaskId`. Every transition path (`StartAsync`, `GetAsync`, `SignalAsync`) writes the value to `WorkItem.CurrentTaskId` opportunistically — same pattern as `CurrentStatus` / `CurrentCheckpointKey` today. `IWorkItemLookup` surfaces the cached value cross-module for the reconciler.

## Implementation Steps

### Step 1: Extend the three executor-response records
**File:** `src/DevHub.Contracts/Executors/ExecutorStartResponse.cs` · Modify
**File:** `src/DevHub.Contracts/Executors/ExecutorFetchResponse.cs` · Modify
**File:** `src/DevHub.Contracts/Executors/ExecutorSignalResponse.cs` · Modify

Each gains a `string? CurrentTaskId` with `[JsonPropertyName("currentTaskId")]`. Position the new field after `CurrentCheckpointKey`. Existing tests deserialize the FakeExecutor's body fine — the field defaults to `null` when absent.

### Step 2: Cache on `WorkItem.CurrentTaskId` on every transition
**File:** `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` · Modify

In `GetAsync` (around line 96, where `CurrentStatus` / `CurrentCheckpointKey` are updated):

```csharp
if (wi.CurrentStatus != resp.CurrentStatus
    || wi.CurrentCheckpointKey != resp.CurrentCheckpointKey
    || wi.CurrentTaskId != resp.CurrentTaskId)
{
    await db.WorkItems
        .Where(w => w.Id == workItemId)
        .ExecuteUpdateAsync(s => s
            .SetProperty(w => w.CurrentStatus, resp.CurrentStatus)
            .SetProperty(w => w.CurrentCheckpointKey, resp.CurrentCheckpointKey)
            .SetProperty(w => w.CurrentTaskId, resp.CurrentTaskId), ct);
}
```

In `StartAsync`, when constructing the new `WorkItem` entity, set `CurrentTaskId = startResp.CurrentTaskId`.

**File:** `src/DevHub.Modules.WorkItems/Services/CheckpointSignalsService.cs` · Modify

Same pattern in the signal-completion path where the work item row is updated from the executor's response.

### Step 3: Extend the DTOs
**File:** `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs` · Modify

`WorkItemDto` and `WorkItemSummaryDto` gain `string? CurrentTaskId` at the end of the positional record. Audit the existing call sites for positional construction (already known: 5 places — `WorkItemsService.ListAsync`, `GetAsync`, `StartAsync`, `UpdateAsync`, `CheckpointSignalsService.SignalAsync`) and update each.

### Step 4: Extend `IWorkItemLookup`
**File:** `src/DevHub.Contracts/WorkItems/IWorkItemLookup.cs` · Modify

The lookup result record (used by the reconciler) gains `string? CurrentTaskId`. Default to `null` on the record so any positional callers compile unchanged.

**File:** `src/DevHub.Modules.WorkItems/Services/WorkItemLookup.cs` (or wherever the implementation lives) · Modify

Add `CurrentTaskId` to the projection and the result construction.

### Step 5: Update `docs/api-spec.md`
**File:** `docs/api-spec.md` · Modify

The WorkItemDto + WorkItemSummaryDto sections gain a `currentTaskId` row. Changelog entry:

```
| 2026-05-17 (FEAT-009 / T-065) | WorkItemDto + WorkItemSummaryDto gained optional currentTaskId. ExecutorStartResponse / ExecutorFetchResponse / ExecutorSignalResponse gained the same field; cached on WorkItem.CurrentTaskId on every transition. |
```

### Step 6: Run the suite
**Bash:**

```bash
dotnet test
```

182/182 still green. The FakeExecutor's existing responses don't include `currentTaskId`, so DevHub stores `null` and behavior is unchanged.

## Files Affected
| File | Action |
|------|--------|
| `src/DevHub.Contracts/Executors/Executor{Start,Fetch,Signal}Response.cs` | Modify |
| `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/CheckpointSignalsService.cs` | Modify |
| `src/DevHub.Contracts/WorkItems/IWorkItemLookup.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/WorkItemLookup.cs` | Modify |
| `docs/api-spec.md` | Modify |

## Edge Cases & Risks
- **Positional `WorkItemDto` callers.** Same risk as T-058. Search for `new WorkItemDto(` and `new WorkItemSummaryDto(` and update every constructor site in lockstep. The compiler catches misses.
- **Staleness window.** Between transitions, `WorkItem.CurrentTaskId` can be stale. The reconciler runs on every transition observed by DevHub, so the window is the same as for `CurrentCheckpointKey` today — acceptable.
- **Executor that doesn't track tasks** keeps sending `currentTaskId: null`. DevHub stores null. Reconciler's per-task logic only activates when the contract has `perTask=true` AND `wi.CurrentTaskId` is non-null; otherwise behavior is byte-for-byte today.

## Acceptance Verification
- [ ] All three executor-response records carry the new field.
- [ ] `WorkItem.CurrentTaskId` is updated on every transition.
- [ ] DTOs round-trip the value.
- [ ] `IWorkItemLookup` exposes it for the reconciler.
- [ ] `dotnet test` is green.
