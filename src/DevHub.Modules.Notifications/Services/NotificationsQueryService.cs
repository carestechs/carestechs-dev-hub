using DevHub.Contracts.Executors;
using DevHub.Contracts.Workspace;
using DevHub.Contracts.WorkItems;
using DevHub.Modules.Notifications.DTOs;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Notifications.Services;

/// <summary>
/// Joins <see cref="Entities.PendingActionSignal"/> rows to the cross-module lookups for the
/// display fields. v1 ships with per-row lookups (acceptable for typical "≤200 pending per
/// member" loads); FEAT-006 can batch via the existing lookup contracts.
/// </summary>
internal sealed class NotificationsQueryService(
    NotificationsDbContext db,
    IProjectLookup projects,
    IWorkItemLookup workItems,
    IExecutorRouter router) : INotificationsQueryService
{
    public async Task<IReadOnlyList<PendingActionDto>> ListPendingForMemberAsync(Guid memberId, CancellationToken ct = default)
    {
        var rows = await db.PendingActionSignals.AsNoTracking()
            .Where(p => p.MemberId == memberId && p.DismissedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        var results = new List<PendingActionDto>(rows.Count);
        foreach (var row in rows)
        {
            var project = await projects.FindByIdAsync(row.ProjectId, ct);
            var wi = await workItems.FindByIdAsync(row.WorkItemId, ct);
            if (project is null || wi is null) continue; // tolerate orphans

            var contract = await router.GetCheckpointContractAsync(wi.ExecutorId, row.CheckpointKey, ct);
            results.Add(new PendingActionDto(
                row.ProjectId, project.Slug,
                row.WorkItemId, wi.Title,
                row.CheckpointKey, contract?.DisplayName ?? row.CheckpointKey,
                row.CreatedAt));
        }
        return results;
    }
}
