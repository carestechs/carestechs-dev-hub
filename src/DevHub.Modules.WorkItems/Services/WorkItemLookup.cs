using DevHub.Contracts.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.WorkItems.Services;

internal sealed class WorkItemLookup(WorkItemsDbContext db) : IWorkItemLookup
{
    public async Task<WorkItemLookupResult?> FindByIdAsync(Guid workItemId, CancellationToken cancellationToken = default)
    {
        return await db.WorkItems.AsNoTracking()
            .Where(w => w.Id == workItemId)
            .Select(w => new WorkItemLookupResult(
                w.Id, w.ProjectId, w.ExecutorId,
                w.CurrentStatus, w.CurrentCheckpointKey, w.Title))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListWaitingForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await db.WorkItems.AsNoTracking()
            .Where(w => w.ProjectId == projectId && w.CurrentStatus == "WaitingOnCheckpoint")
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);
    }
}
