using System.Text.Json;
using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Contracts.Executors;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Notifications;
using DevHub.Contracts.Pagination;
using DevHub.Modules.WorkItems.DTOs;
using DevHub.Modules.WorkItems.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.WorkItems.Services;

internal sealed class CheckpointSignalsService(
    WorkItemsDbContext db,
    IProjectAuthorizationService authz,
    IExecutorRouter router,
    IExecutorClientFactory clientFactory,
    IMemberLookup members,
    IAuditWriter audit,
    IWorkItemsService workItemsService,
    IPendingActionReconciler reconciler) : ICheckpointSignalsService
{
    public async Task<WorkItemDto> SignalAsync(
        Guid projectId, Guid workItemId, string checkpointKey, SignalRequest request,
        string? idempotencyKey, Guid actingMemberId, CancellationToken ct)
    {
        var wi = await db.WorkItems.FirstOrDefaultAsync(w => w.Id == workItemId && w.ProjectId == projectId, ct)
            ?? throw new NotFoundException("Work item not found.");

        var descriptor = await router.ResolveAsync(projectId, ct)
            ?? throw new ConflictException("Project has no executor bound.");

        var contract = await router.GetCheckpointContractAsync(descriptor.Id, checkpointKey, ct)
            ?? throw new NotFoundException($"Checkpoint '{checkpointKey}' is not defined on this executor.");

        // Authorize FIRST so an unauthorized caller cannot probe allowedOutcomes via 400 responses.
        await authz.EnsureAuthorizedAsync(actingMemberId, projectId, "workitem:signal", contract.RequiredRoleKey, ct);

        if (!contract.AllowedOutcomes.Contains(request.Outcome))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["outcome"] = new[] { $"Outcome '{request.Outcome}' is not allowed for this checkpoint." },
            });
        }

        // FEAT-009 / T-068: assignment-confirmed (the first per-task checkpoint) requires a
        // non-empty payload.assignee at the DevHub boundary. The executor's idempotency hash
        // is (run_id, "assignment-confirmed", task_id) and treats assignee as opaque — but
        // an empty assignee would slip through to the executor and produce a useless audit
        // trail. Reject at the boundary before the executor call.
        var assignee = ExtractAssignee(request.Payload);
        if (contract.PerTask && checkpointKey == "assignment-confirmed" && string.IsNullOrWhiteSpace(assignee))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["payload.assignee"] = new[]
                {
                    "payload.assignee must be a non-empty string for assignment-confirmed signals.",
                },
            });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Idempotency replay.
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var prior = await db.CheckpointSignals
                .Where(s => s.WorkItemId == workItemId && s.IdempotencyKey == idempotencyKey)
                .OrderByDescending(s => s.SignaledAt)
                .FirstOrDefaultAsync(ct);
            if (prior is not null)
            {
                await tx.RollbackAsync(ct);
                return await workItemsService.GetAsync(projectId, workItemId, actingMemberId, ct);
            }
        }

        if (wi.CurrentStatus != "WaitingOnCheckpoint" || wi.CurrentCheckpointKey != checkpointKey)
        {
            throw new ConflictException(
                $"Work item is not waiting on checkpoint '{checkpointKey}' (current: {wi.CurrentStatus}/{wi.CurrentCheckpointKey ?? "none"}).");
        }

        var signal = new CheckpointSignal
        {
            WorkItemId = workItemId,
            CheckpointKey = checkpointKey,
            Outcome = request.Outcome,
            PayloadJson = request.Payload?.GetRawText(),
            SignaledByMemberId = actingMemberId,
            SignaledAt = DateTimeOffset.UtcNow,
            IdempotencyKey = idempotencyKey,
        };
        db.CheckpointSignals.Add(signal);

        ExecutorSignalResponse signalResp;
        var executorClient = clientFactory.Resolve(descriptor);
        var workItemRef = new WorkItemRef(wi.ExecutorCorrelationMarker, wi.ExecutorRunId);
        try
        {
            signalResp = await executorClient.SignalAsync(
                descriptor, workItemRef, checkpointKey, request.Outcome, request.Payload, request.TaskId, ct);
        }
        catch (ExecutorFailureException ex)
        {
            await audit.WriteAsync(new AuditWriteRequest("CheckpointSignal", signal.Id, "checkpoint:signal", AuditOutcome.Failed)
            {
                ActingMemberId = actingMemberId,
                ProjectId = projectId,
                Reason = "executor failure",
                Details = WorkItemsService.ExecutorFailureDetails(ex, wi.ExecutorCorrelationMarker),
            }, ct);
            // Persist the signal row with null executor_response_status (FEAT-006 surfaces stuck signals).
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw;
        }

        signal.ExecutorResponseStatus = signalResp.HttpStatus;
        signal.ExecutorResponseAt = DateTimeOffset.UtcNow;

        // Refresh the cache.
        wi.CurrentStatus = signalResp.CurrentStatus;
        wi.CurrentCheckpointKey = signalResp.CurrentCheckpointKey;
        wi.CurrentTaskId = signalResp.CurrentTaskId;

        // Persist prUrl from implementation-complete payload (FEAT-012).
        var prUrl = checkpointKey == "implementation-complete" ? ExtractPrUrl(request.Payload) : null;
        if (prUrl is not null)
            wi.PrUrl = prUrl;

        await audit.WriteAsync(new AuditWriteRequest("CheckpointSignal", signal.Id, "checkpoint:signal", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            ProjectId = projectId,
            Details = new Dictionary<string, object?>
            {
                ["executorKey"] = descriptor.Key,
                ["executorCorrelationMarker"] = wi.ExecutorCorrelationMarker,
                ["checkpointKey"] = checkpointKey,
                ["outcome"] = request.Outcome,
                ["idempotencyKey"] = idempotencyKey,
                ["executorResponseStatus"] = signalResp.HttpStatus,
                ["taskId"] = request.TaskId,
                ["assignee"] = assignee,
                ["prUrl"] = prUrl,
            },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Post-commit: status moved (terminal or different checkpoint) — recompute.
        await reconciler.RecomputeForWorkItemAsync(workItemId, ct);

        var createdBy = await members.FindByIdAsync(wi.CreatedByMemberId, ct);
        return new WorkItemDto(
            wi.Id, wi.ProjectId, wi.Title, wi.CurrentStatus, wi.CurrentCheckpointKey,
            new ExecutorRefDto(descriptor.Id, descriptor.Key, descriptor.DisplayName),
            wi.ExecutorCorrelationMarker, wi.CreatedAt,
            createdBy is null
                ? new MemberRefDto(wi.CreatedByMemberId, "(unknown)")
                : new MemberRefDto(createdBy.Id, createdBy.DisplayName),
            signalResp.ExecutorState,
            wi.WorkBranch,
            wi.CurrentTaskId,
            wi.ExecutorRunId,
            wi.PrUrl);
    }

    public async Task<PagedEnvelopeDto<CheckpointSignalDto>> ListSignalsAsync(
        Guid projectId, Guid workItemId, PageRequest page, Guid currentMemberId, CancellationToken ct)
    {
        await authz.EnsureAuthorizedAsync(currentMemberId, projectId, "workitem:signals:list", requiredRoleKey: null, ct);

        // Verify the work item belongs to this project (FK is by id only; we authorize by project).
        var exists = await db.WorkItems
            .AnyAsync(w => w.Id == workItemId && w.ProjectId == projectId, ct);
        if (!exists) throw new NotFoundException("Work item not found.");

        IQueryable<CheckpointSignal> q = db.CheckpointSignals.AsNoTracking()
            .Where(s => s.WorkItemId == workItemId)
            .OrderByDescending(s => s.SignaledAt);

        var totalCount = await q.CountAsync(ct);
        var rows = await q
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(ct);

        var memberIds = rows.Select(r => r.SignaledByMemberId).Distinct().ToList();
        var memberMap = new Dictionary<Guid, MemberRefDto>();
        foreach (var id in memberIds)
        {
            var m = await members.FindByIdAsync(id, ct);
            memberMap[id] = m is null
                ? new MemberRefDto(id, "(unknown)")
                : new MemberRefDto(m.Id, m.DisplayName);
        }

        var dtos = rows.Select(s => new CheckpointSignalDto(
            s.Id, s.CheckpointKey, s.Outcome,
            memberMap[s.SignaledByMemberId],
            s.SignaledAt, s.ExecutorResponseStatus,
            s.PayloadJson is null ? null : JsonDocument.Parse(s.PayloadJson).RootElement)).ToList();

        return new PagedEnvelopeDto<CheckpointSignalDto>(dtos,
            new PageMeta(totalCount, page.Page, page.PageSize, page.SortBy, page.SortDir));
    }

    /// <summary>
    /// Reads <c>payload.assignee</c> from the signal's payload as a string. Returns null
    /// when the payload is absent, isn't an object, doesn't carry an <c>assignee</c> key,
    /// or carries a non-string value. The boundary guard in <see cref="SignalAsync"/>
    /// checks for non-empty; this method is shape-only.
    /// </summary>
    private static string? ExtractAssignee(JsonElement? payload)
    {
        if (payload is null) return null;
        var p = payload.Value;
        if (p.ValueKind != JsonValueKind.Object) return null;
        if (!p.TryGetProperty("assignee", out var a)) return null;
        return a.ValueKind == JsonValueKind.String ? a.GetString() : null;
    }

    private static string? ExtractPrUrl(JsonElement? payload)
    {
        if (payload is null) return null;
        var p = payload.Value;
        if (p.ValueKind != JsonValueKind.Object) return null;
        if (!p.TryGetProperty("prUrl", out var u)) return null;
        var val = u.ValueKind == JsonValueKind.String ? u.GetString() : null;
        return string.IsNullOrWhiteSpace(val) ? null : val;
    }
}
