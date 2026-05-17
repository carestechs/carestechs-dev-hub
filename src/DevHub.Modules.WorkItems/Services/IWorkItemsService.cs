using DevHub.Contracts.Pagination;
using DevHub.Modules.WorkItems.DTOs;

namespace DevHub.Modules.WorkItems.Services;

public interface IWorkItemsService
{
    Task<PagedEnvelopeDto<WorkItemSummaryDto>> ListAsync(
        Guid projectId, PageRequest page, string? statusFilter, bool waitingOnMe, Guid currentMemberId, CancellationToken ct);

    Task<WorkItemDto> GetAsync(Guid projectId, Guid workItemId, Guid currentMemberId, CancellationToken ct);

    Task<WorkItemDto> StartAsync(Guid projectId, StartWorkItemRequest request, Guid actingMemberId, CancellationToken ct);

    Task<WorkItemDto> UpdateAsync(Guid projectId, Guid workItemId, UpdateWorkItemRequest request, Guid actingMemberId, CancellationToken ct);

    Task CancelAsync(Guid projectId, Guid workItemId, Guid actingMemberId, CancellationToken ct);
}

public interface ICheckpointSignalsService
{
    Task<WorkItemDto> SignalAsync(
        Guid projectId, Guid workItemId, string checkpointKey, SignalRequest request,
        string? idempotencyKey, Guid actingMemberId, CancellationToken ct);

    Task<PagedEnvelopeDto<CheckpointSignalDto>> ListSignalsAsync(
        Guid projectId, Guid workItemId, PageRequest page, Guid currentMemberId, CancellationToken ct);
}
