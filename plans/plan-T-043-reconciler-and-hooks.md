# Implementation Plan: T-043 — IPendingActionReconciler + WorkItems hook + Workspace backfill

## Task Reference
- **Task ID:** T-043 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-1 (≤2s) and AC-2 (dismiss on resolve) fall out of "compute synchronously on transition." Workspace backfill covers "member added to a project with pending checkpoints."

## Overview
Three deliverables in one PR:
1. **Cross-module contract**: `IPendingActionReconciler` published from `DevHub.Contracts`.
2. **Notifications implementation**: `PendingActionReconciler` that upserts/dismisses rows and publishes events to a per-member channel registry. Idempotent.
3. **Consumer hooks**: WorkItems calls the reconciler inside its existing transactions; Workspace calls it on membership changes.

## Implementation Steps

### Step 1: Contracts
**Files (Create):**
- `src/DevHub.Contracts/Notifications/IPendingActionReconciler.cs`
- `src/DevHub.Contracts/Notifications/PendingActionEvent.cs`

```csharp
public interface IPendingActionReconciler
{
    Task RecomputeForWorkItemAsync(Guid workItemId, CancellationToken ct = default);
    Task RecomputeForMemberInProjectAsync(Guid memberId, Guid projectId, CancellationToken ct = default);
}

public sealed record PendingActionEvent(
    string Kind,         // "raised" | "dismissed"
    Guid MemberId,
    Guid ProjectId,
    Guid WorkItemId,
    string CheckpointKey,
    DateTimeOffset OccurredAt);
```

`RecomputeForWorkItemAsync(workItemId)` is the hot-path call; it figures out projectId itself by reading the work item. Keeps the WorkItems caller surface tight.

### Step 2: Membership query extensions
Workspace already publishes `IProjectMembershipQuery`. Extend it (or add a sibling interface in Contracts) with:
- `Task<IReadOnlyList<Guid>> GetMembersWithRoleAsync(Guid projectId, string roleKey, CancellationToken ct)`
- `Task<IReadOnlyList<Guid>> GetWorkspaceOperatorsAsync(CancellationToken ct)`

Implement on `ProjectMembershipQuery` in Workspace. Operator query joins `WorkspaceRoleAssignment` to `Role` where `role.key == "operator"`. Per-project query joins `ProjectMembership` + `RoleAssignment` + `Role`.

### Step 3: Cross-module work item lookup
Add `IWorkItemLookup` in `DevHub.Contracts.WorkItems` so the reconciler can read `(projectId, executorId, currentStatus, currentCheckpointKey)` for a work item without depending on the WorkItems module.

**File:** `src/DevHub.Contracts/WorkItems/IWorkItemLookup.cs` · Create

```csharp
public interface IWorkItemLookup
{
    Task<WorkItemLookupResult?> FindByIdAsync(Guid workItemId, CancellationToken ct = default);
}
public sealed record WorkItemLookupResult(
    Guid Id, Guid ProjectId, Guid ExecutorId,
    string CurrentStatus, string? CurrentCheckpointKey, string Title);
```

Implementation in WorkItems module: `WorkItemLookup` over `WorkItemsDbContext`.

### Step 4: Stream registry
**File:** `src/DevHub.Modules.Notifications/Services/PendingActionStreamRegistry.cs` · Create

```csharp
public sealed class PendingActionStreamRegistry
{
    private readonly ConcurrentDictionary<Guid, ConcurrentBag<Channel<PendingActionEvent>>> _subs = new();

    public IAsyncDisposable Subscribe(Guid memberId, out ChannelReader<PendingActionEvent> reader)
    {
        var channel = Channel.CreateUnbounded<PendingActionEvent>(new UnboundedChannelOptions { SingleReader = true });
        reader = channel.Reader;
        var bag = _subs.GetOrAdd(memberId, _ => new());
        bag.Add(channel);
        return new Cleanup(this, memberId, channel);
    }

    public void Publish(PendingActionEvent ev)
    {
        if (_subs.TryGetValue(ev.MemberId, out var bag))
            foreach (var ch in bag) ch.Writer.TryWrite(ev);
    }

    private sealed class Cleanup(PendingActionStreamRegistry owner, Guid memberId, Channel<PendingActionEvent> ch) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            ch.Writer.TryComplete();
            if (owner._subs.TryGetValue(memberId, out var bag))
            {
                // ConcurrentBag doesn't support removal; we keep the slot and rely on TryComplete +
                // SSE reader exit. For v1 this is acceptable; the bag holds completed channels
                // until the member id falls out of the dict on app restart.
            }
            return ValueTask.CompletedTask;
        }
    }
}
```

Registered as **singleton** — it's process-wide state.

> **v1 single-host caveat (document at top of the file):** This registry holds in-process channels. Multi-host fan-out is a v2 concern.

### Step 5: Reconciler
**File:** `src/DevHub.Modules.Notifications/Services/PendingActionReconciler.cs` · Create

```csharp
internal sealed class PendingActionReconciler(
    NotificationsDbContext db,
    IWorkItemLookup workItems,
    IExecutorRouter router,
    IProjectMembershipQuery memberships,
    PendingActionStreamRegistry registry) : IPendingActionReconciler
{
    public async Task RecomputeForWorkItemAsync(Guid workItemId, CancellationToken ct = default)
    {
        var wi = await workItems.FindByIdAsync(workItemId, ct);
        if (wi is null) { await DismissAllForWorkItemAsync(workItemId, ct); return; }

        var isWaiting = wi.CurrentStatus == "WaitingOnCheckpoint" && wi.CurrentCheckpointKey is not null;
        if (!isWaiting) { await DismissAllForWorkItemAsync(workItemId, ct); return; }

        var contract = await router.GetCheckpointContractAsync(wi.ExecutorId, wi.CurrentCheckpointKey!, ct);
        if (contract is null) { await DismissAllForWorkItemAsync(workItemId, ct); return; }

        var requiredMembers = (await memberships.GetMembersWithRoleAsync(wi.ProjectId, contract.RequiredRoleKey, ct)).ToHashSet();
        foreach (var op in await memberships.GetWorkspaceOperatorsAsync(ct)) requiredMembers.Add(op);

        // Upsert: for each member in the set, ensure a non-dismissed row exists for (member, workItem, checkpointKey).
        var existing = await db.PendingActionSignals
            .Where(p => p.WorkItemId == workItemId && p.CheckpointKey == wi.CurrentCheckpointKey && p.DismissedAt == null)
            .ToListAsync(ct);
        var existingByMember = existing.ToDictionary(p => p.MemberId);

        var now = DateTimeOffset.UtcNow;
        // Raise rows for members in the required set who don't have one.
        foreach (var memberId in requiredMembers)
        {
            if (existingByMember.ContainsKey(memberId)) continue;
            var row = new PendingActionSignal
            {
                MemberId = memberId,
                ProjectId = wi.ProjectId,
                WorkItemId = workItemId,
                CheckpointKey = wi.CurrentCheckpointKey!,
            };
            db.PendingActionSignals.Add(row);
            registry.Publish(new PendingActionEvent("raised", memberId, wi.ProjectId, workItemId, wi.CurrentCheckpointKey!, now));
        }
        // Dismiss rows for members no longer in the set OR for stale checkpoint keys.
        foreach (var (memberId, row) in existingByMember)
        {
            if (requiredMembers.Contains(memberId)) continue;
            row.DismissedAt = now;
            registry.Publish(new PendingActionEvent("dismissed", memberId, wi.ProjectId, workItemId, wi.CurrentCheckpointKey!, now));
        }
        // Stale-checkpoint rows for OTHER checkpoint keys on the same work item:
        var stale = await db.PendingActionSignals
            .Where(p => p.WorkItemId == workItemId && p.CheckpointKey != wi.CurrentCheckpointKey && p.DismissedAt == null)
            .ToListAsync(ct);
        foreach (var row in stale)
        {
            row.DismissedAt = now;
            registry.Publish(new PendingActionEvent("dismissed", row.MemberId, row.ProjectId, row.WorkItemId, row.CheckpointKey, now));
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RecomputeForMemberInProjectAsync(Guid memberId, Guid projectId, CancellationToken ct = default)
    {
        // Cheap shape for v1: read all WaitingOnCheckpoint work items in the project and call
        // RecomputeForWorkItemAsync for each (they're idempotent). Smaller projects only; FEAT-006
        // can optimize.
        var workItemIds = await ((IWorkItemLookupBatched)workItems)
            .ListWaitingForProjectAsync(projectId, ct);
        foreach (var id in workItemIds) await RecomputeForWorkItemAsync(id, ct);
    }

    private async Task DismissAllForWorkItemAsync(Guid workItemId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await db.PendingActionSignals
            .Where(p => p.WorkItemId == workItemId && p.DismissedAt == null)
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            row.DismissedAt = now;
            registry.Publish(new PendingActionEvent("dismissed", row.MemberId, row.ProjectId, row.WorkItemId, row.CheckpointKey, now));
        }
        if (rows.Count > 0) await db.SaveChangesAsync(ct);
    }
}
```

`IWorkItemLookupBatched` extends `IWorkItemLookup` with `ListWaitingForProjectAsync(projectId)`. Implementation in WorkItems.

### Step 6: WorkItems hooks
Inside `WorkItemsService.StartAsync` and `WorkItemsService.CancelAsync`, and `CheckpointSignalsService.SignalAsync`, **after** writing the Granted audit row and **before** `tx.CommitAsync`:

```csharp
await reconciler.RecomputeForWorkItemAsync(workItem.Id, ct);
```

Inject `IPendingActionReconciler` into both services.

### Step 7: Workspace backfill
Inside `MembershipService.Add` and `MembershipService.Update`, after the existing commit, call:

```csharp
await reconciler.RecomputeForMemberInProjectAsync(memberId, projectId, ct);
```

The call is out-of-transaction because the membership write needs to be visible to the reconciler's role lookup. Acceptable: a sub-second window where the member is on the project but doesn't yet see backfill — they'll see it on the next stream tick anyway.

### Step 8: DI
**File:** `src/DevHub.Modules.Notifications/NotificationsModuleExtensions.cs` · Modify
```csharp
services.AddScoped<IPendingActionReconciler, PendingActionReconciler>();
services.AddSingleton<PendingActionStreamRegistry>();
```

**File:** `src/DevHub.Modules.WorkItems/WorkItemsModuleExtensions.cs` · Modify
Register `IWorkItemLookup` + `IWorkItemLookupBatched`.

**File:** `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs` · Verify
`IProjectMembershipQuery` is already registered.

## Files Affected
| File | Action |
|------|--------|
| `Contracts/Notifications/IPendingActionReconciler.cs`, `PendingActionEvent.cs` | Create |
| `Contracts/WorkItems/IWorkItemLookup.cs` | Create |
| `Modules.WorkItems/Services/WorkItemLookup.cs` | Create |
| `Modules.Notifications/Services/PendingActionReconciler.cs`, `PendingActionStreamRegistry.cs` | Create |
| `Modules.WorkItems/Services/WorkItemsService.cs`, `CheckpointSignalsService.cs` | Modify (call reconciler) |
| `Modules.Workspace/Services/MembershipService.cs` | Modify (backfill call) |
| `Modules.Workspace/Services/ProjectMembershipQuery.cs` | Modify (new query methods) |
| `Contracts/Authorization/IProjectMembershipQuery.cs` | Modify (new method signatures) |
| Module extensions | Modify (DI) |

## Edge Cases & Risks
- **Reconciler on hot path.** Tiny role sets in v1; ship a metrics counter for reconciler runtime so FEAT-006 can surface it.
- **Concurrent transitions on the same work item.** Both transactions read-then-write `PendingActionSignal`; the partial-unique index on `(member_id, work_item_id, checkpoint_key) WHERE dismissed_at IS NULL` is the safety net. EF Core's `SaveChangesAsync` will surface a `DbUpdateException` on conflict; we can retry once or document the race.
- **`registry.Publish` ordering vs DB commit.** We publish to the channel *before* `SaveChangesAsync`. If the save fails, subscribers see an event for a row that doesn't exist. Reorder: collect events into a local list, save first, then publish.
- **Operator inbox volume.** Documented.

## Acceptance Verification
- [ ] `dotnet build` clean.
- [ ] Existing tests still pass (the reconciler call is a no-op on tests that don't seed checkpoint contracts — the contract resolves to `null` and the reconciler short-circuits).
- [ ] T-045's reconciler unit tests cover the matrix.
