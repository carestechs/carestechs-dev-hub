using DevHub.Contracts.Authorization;
using DevHub.Contracts.Executors;
using DevHub.Contracts.Notifications;
using DevHub.Contracts.WorkItems;
using DevHub.Modules.Notifications.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Notifications.Services;

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

        // If the work item doesn't exist or isn't waiting on a checkpoint, dismiss everything for it.
        if (wi is null
            || wi.CurrentStatus != "WaitingOnCheckpoint"
            || wi.CurrentCheckpointKey is null)
        {
            await DismissAllForWorkItemAsync(workItemId, ct);
            return;
        }

        var contract = await router.GetCheckpointContractAsync(wi.ExecutorId, wi.CurrentCheckpointKey, ct);
        if (contract is null)
        {
            // Executor doesn't (any longer) declare this checkpoint — fall back to dismissing all.
            await DismissAllForWorkItemAsync(workItemId, ct);
            return;
        }

        var requiredMembers = (await memberships.GetMembersWithRoleAsync(wi.ProjectId, contract.RequiredRoleKey, ct))
            .ToHashSet();
        foreach (var op in await memberships.GetWorkspaceOperatorsAsync(ct))
        {
            requiredMembers.Add(op);
        }

        var existingForActiveKey = await db.PendingActionSignals
            .Where(p => p.WorkItemId == workItemId
                        && p.CheckpointKey == wi.CurrentCheckpointKey
                        && p.DismissedAt == null)
            .ToListAsync(ct);
        var existingByMember = existingForActiveKey.ToDictionary(p => p.MemberId);

        var staleForOtherKeys = await db.PendingActionSignals
            .Where(p => p.WorkItemId == workItemId
                        && p.CheckpointKey != wi.CurrentCheckpointKey
                        && p.DismissedAt == null)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var pending = new List<PendingActionEvent>();

        // Raise rows for required members who don't already have one.
        foreach (var memberId in requiredMembers)
        {
            if (existingByMember.ContainsKey(memberId)) continue;
            db.PendingActionSignals.Add(new PendingActionSignal
            {
                MemberId = memberId,
                ProjectId = wi.ProjectId,
                WorkItemId = workItemId,
                CheckpointKey = wi.CurrentCheckpointKey,
            });
            pending.Add(new PendingActionEvent("raised", memberId, wi.ProjectId, workItemId, wi.CurrentCheckpointKey, now));
        }

        // Dismiss rows for members no longer in the required set (e.g., lost role).
        foreach (var (memberId, row) in existingByMember)
        {
            if (requiredMembers.Contains(memberId)) continue;
            row.DismissedAt = now;
            pending.Add(new PendingActionEvent("dismissed", memberId, wi.ProjectId, workItemId, row.CheckpointKey, now));
        }

        // Dismiss rows for stale checkpoint keys (status moved from one checkpoint to another).
        foreach (var row in staleForOtherKeys)
        {
            row.DismissedAt = now;
            pending.Add(new PendingActionEvent("dismissed", row.MemberId, row.ProjectId, row.WorkItemId, row.CheckpointKey, now));
        }

        if (pending.Count == 0) return;

        await db.SaveChangesAsync(ct);
        foreach (var ev in pending) registry.Publish(ev);
    }

    public async Task RecomputeForMemberInProjectAsync(Guid memberId, Guid projectId, CancellationToken ct = default)
    {
        // Re-running per-work-item is idempotent; the new member appears in the required set on
        // the second pass without us teaching the per-work-item path about specific members.
        var waitingIds = await workItems.ListWaitingForProjectAsync(projectId, ct);
        foreach (var id in waitingIds)
        {
            await RecomputeForWorkItemAsync(id, ct);
        }
    }

    private async Task DismissAllForWorkItemAsync(Guid workItemId, CancellationToken ct)
    {
        var rows = await db.PendingActionSignals
            .Where(p => p.WorkItemId == workItemId && p.DismissedAt == null)
            .ToListAsync(ct);

        if (rows.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        var pending = new List<PendingActionEvent>(rows.Count);
        foreach (var row in rows)
        {
            row.DismissedAt = now;
            pending.Add(new PendingActionEvent("dismissed", row.MemberId, row.ProjectId, row.WorkItemId, row.CheckpointKey, now));
        }

        await db.SaveChangesAsync(ct);
        foreach (var ev in pending) registry.Publish(ev);
    }
}
