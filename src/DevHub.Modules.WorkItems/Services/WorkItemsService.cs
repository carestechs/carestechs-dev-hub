using System.Text.Json;
using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Contracts.Executors;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Notifications;
using DevHub.Contracts.Pagination;
using DevHub.Contracts.Validation;
using DevHub.Contracts.Workspace;
using DevHub.Modules.WorkItems.DTOs;
using DevHub.Modules.WorkItems.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevHub.Modules.WorkItems.Services;

internal sealed class WorkItemsService(
    WorkItemsDbContext db,
    IProjectAuthorizationService authz,
    IExecutorRouter router,
    IExecutorHttpClient executorClient,
    IMemberLookup members,
    IProjectLookup projects,
    IAuditWriter audit,
    IPendingActionReconciler reconciler,
    ILogger<WorkItemsService> log) : IWorkItemsService
{
    private const string StartCheckpointKey = "start";
    private const string CancelCheckpointKey = "cancel";

    public async Task<PagedEnvelopeDto<WorkItemSummaryDto>> ListAsync(
        Guid projectId, PageRequest page, string? statusFilter, bool waitingOnMe,
        Guid currentMemberId, CancellationToken ct)
    {
        await authz.EnsureAuthorizedAsync(currentMemberId, projectId, "workitem:list", requiredRoleKey: null, ct);

        IQueryable<WorkItem> q = db.WorkItems.AsNoTracking().Where(w => w.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            var statuses = statusFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            q = q.Where(w => statuses.Contains(w.CurrentStatus));
        }
        // waitingOnMe is wired in FEAT-005 via PendingActionSignal; for now we no-op and document.
        _ = waitingOnMe;

        var totalCount = await q.CountAsync(ct);

        q = (page.SortBy?.ToLowerInvariant(), page.SortDir) switch
        {
            ("title", "asc") => q.OrderBy(w => w.Title),
            ("title", _)     => q.OrderByDescending(w => w.Title),
            ("updatedat", "asc") => q.OrderBy(w => w.UpdatedAt),
            ("updatedat", _)   => q.OrderByDescending(w => w.UpdatedAt),
            (_, "asc")         => q.OrderBy(w => w.CreatedAt),
            _                  => q.OrderByDescending(w => w.CreatedAt),
        };

        var rows = await q
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(ct);

        // All rows in this project share the same bound executor (FEAT-003 disallows rebinding
        // in-flight projects). One Resolve covers everyone.
        var memberNames = await ResolveMembersAsync(rows.Select(r => r.CreatedByMemberId).Distinct(), ct);
        var descriptor = rows.Count == 0
            ? null
            : await router.ResolveAsync(projectId, ct);
        var executorRef = descriptor is null
            ? null
            : new ExecutorRefDto(descriptor.Id, descriptor.Key, descriptor.DisplayName);

        var dtos = rows.Select(r => new WorkItemSummaryDto(
            r.Id, r.ProjectId, r.Title, r.CurrentStatus, r.CurrentCheckpointKey,
            executorRef ?? new ExecutorRefDto(r.ExecutorId, "executor", "Executor"),
            r.ExecutorCorrelationMarker, r.CreatedAt,
            memberNames[r.CreatedByMemberId],
            r.WorkBranch,
            r.CurrentTaskId)).ToList();

        return new PagedEnvelopeDto<WorkItemSummaryDto>(dtos,
            new PageMeta(totalCount, page.Page, page.PageSize, page.SortBy, page.SortDir));
    }

    public async Task<WorkItemDto> GetAsync(Guid projectId, Guid workItemId, Guid currentMemberId, CancellationToken ct)
    {
        await authz.EnsureAuthorizedAsync(currentMemberId, projectId, "workitem:read", requiredRoleKey: null, ct);

        var wi = await db.WorkItems.FirstOrDefaultAsync(w => w.Id == workItemId && w.ProjectId == projectId, ct)
            ?? throw new NotFoundException("Work item not found.");

        var descriptor = await router.ResolveAsync(projectId, ct)
            ?? throw new ConflictException("Project has no executor bound.");

        // Fetch latest state from the executor and refresh the cache opportunistically.
        // ExecutorFailureException propagates as 502 via the global handler.
        var resp = await executorClient.FetchStateAsync(descriptor, wi.ExecutorCorrelationMarker, ct);

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

        var createdBy = await members.FindByIdAsync(wi.CreatedByMemberId, ct);
        return new WorkItemDto(
            wi.Id, wi.ProjectId, wi.Title, resp.CurrentStatus, resp.CurrentCheckpointKey,
            new ExecutorRefDto(descriptor.Id, descriptor.Key, descriptor.DisplayName),
            wi.ExecutorCorrelationMarker, wi.CreatedAt,
            createdBy is null
                ? new MemberRefDto(wi.CreatedByMemberId, "(unknown)")
                : new MemberRefDto(createdBy.Id, createdBy.DisplayName),
            resp.ExecutorState,
            wi.WorkBranch,
            resp.CurrentTaskId);
    }

    public async Task<WorkItemDto> StartAsync(
        Guid projectId, StartWorkItemRequest request, Guid actingMemberId, CancellationToken ct)
    {
        // Project must exist; resolve descriptor first so the start-role comes from the contract.
        var project = await projects.FindByIdAsync(projectId, ct)
            ?? throw new NotFoundException("Project not found.");

        var descriptor = await router.ResolveAsync(projectId, ct)
            ?? throw new ConflictException("Project has no executor bound.");

        var startContract = descriptor.Contracts.FirstOrDefault(c => c.CheckpointKey == StartCheckpointKey);
        var requiredRole = startContract?.RequiredRoleKey ?? "operator";

        await authz.EnsureAuthorizedAsync(actingMemberId, projectId, "workitem:start", requiredRole, ct);

        // Boundary validation for the optional per-work-item branch override.
        // Empty string is rejected at start time (operators clear via PATCH instead).
        if (request.WorkBranch is not null)
            CodeSourceValidator.ValidateBranch(request.WorkBranch, fieldName: "workBranch");

        // Build intake.codeSource from project + work item. Both repo + defaultBranch must
        // be present on the project; partial coordinates would be invalid on the executor
        // side, so omit the whole block and log for grep-ability (FEAT-008 / IMP-004
        // deprecation timer).
        CodeSourcePayload? codeSource = null;
        if (!string.IsNullOrEmpty(project.Repo) && !string.IsNullOrEmpty(project.DefaultBranch))
        {
            codeSource = new CodeSourcePayload(
                Repo: project.Repo,
                BaseBranch: project.DefaultBranch,
                WorkBranch: request.WorkBranch);
        }
        else
        {
            log.LogInformation(
                "codeSourceMissing=true projectId={ProjectId} — orchestrator IMP-004 deprecation timer applies",
                projectId);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var marker = Guid.NewGuid().ToString("N");
        ExecutorStartResponse startResp;
        try
        {
            startResp = await executorClient.StartAsync(descriptor, marker, request.Input, codeSource, ct);
        }
        catch (ExecutorFailureException ex)
        {
            await audit.WriteAsync(new AuditWriteRequest("WorkItem", null, "workitem:start", AuditOutcome.Failed)
            {
                ActingMemberId = actingMemberId,
                ProjectId = projectId,
                Reason = "executor failure",
                Details = ExecutorFailureDetails(ex, marker),
            }, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw;
        }

        var workItem = new WorkItem
        {
            ProjectId = projectId,
            ExecutorId = descriptor.Id,
            ExecutorCorrelationMarker = marker,
            Title = request.Title,
            CurrentStatus = startResp.CurrentStatus,
            CurrentCheckpointKey = startResp.CurrentCheckpointKey,
            CurrentTaskId = startResp.CurrentTaskId,
            CreatedByMemberId = actingMemberId,
            WorkBranch = request.WorkBranch,
        };
        db.WorkItems.Add(workItem);

        await audit.WriteAsync(new AuditWriteRequest("WorkItem", workItem.Id, "workitem:start", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            ProjectId = projectId,
            Details = new Dictionary<string, object?>
            {
                ["executorKey"] = descriptor.Key,
                ["executorCorrelationMarker"] = marker,
                ["title"] = request.Title,
            },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Post-commit reconciliation: the new work item row needs to be visible to the lookup,
        // which only happens after the tx commits. Failure here doesn't roll back the start —
        // the next transition will recompute (the reconciler is idempotent).
        await reconciler.RecomputeForWorkItemAsync(workItem.Id, ct);

        var actor = await members.FindByIdAsync(actingMemberId, ct);
        return new WorkItemDto(
            workItem.Id, projectId, workItem.Title, workItem.CurrentStatus, workItem.CurrentCheckpointKey,
            new ExecutorRefDto(descriptor.Id, descriptor.Key, descriptor.DisplayName),
            marker, workItem.CreatedAt,
            actor is null
                ? new MemberRefDto(actingMemberId, "(unknown)")
                : new MemberRefDto(actor.Id, actor.DisplayName),
            startResp.ExecutorState,
            workItem.WorkBranch,
            workItem.CurrentTaskId);
    }

    public async Task<WorkItemDto> UpdateAsync(
        Guid projectId, Guid workItemId, UpdateWorkItemRequest request, Guid actingMemberId, CancellationToken ct)
    {
        // v1: operator-only. Future FEATs may relax to project members with the
        // bound 'update' role, but the workitem:update authz key only needs to
        // exist as a string today — EnsureOperatorAsync covers it.
        await authz.EnsureOperatorAsync(actingMemberId, "workitem:update", ct);

        // Validate non-null, non-empty values. Empty string means "clear" (handled below).
        if (!string.IsNullOrEmpty(request.WorkBranch))
            CodeSourceValidator.ValidateBranch(request.WorkBranch, fieldName: "workBranch");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var wi = await db.WorkItems.FirstOrDefaultAsync(w => w.Id == workItemId && w.ProjectId == projectId, ct)
            ?? throw new NotFoundException("Work item not found.");

        var before = wi.WorkBranch;
        // null = leave unchanged, "" = clear, otherwise = set.
        wi.WorkBranch = request.WorkBranch switch
        {
            null => wi.WorkBranch,
            "" => null,
            _ => request.WorkBranch,
        };

        var details = new Dictionary<string, object?>();
        if (before != wi.WorkBranch)
        {
            details["workBranchBefore"] = before;
            details["workBranchAfter"] = wi.WorkBranch;
        }

        if (details.Count > 0)
        {
            await audit.WriteAsync(new AuditWriteRequest("WorkItem", wi.Id, "workitem:update", AuditOutcome.Granted)
            {
                ActingMemberId = actingMemberId,
                ProjectId = projectId,
                Details = details,
            }, ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // The work item's branch change does not affect any in-flight executor run;
        // the value was forwarded at start time and the executor doesn't refetch.
        var descriptor = await router.ResolveAsync(projectId, ct)
            ?? throw new ConflictException("Project has no executor bound.");
        var createdBy = await members.FindByIdAsync(wi.CreatedByMemberId, ct);
        // ExecutorState — not refetched on update; clients call GET to refresh. We return
        // an empty JSON object rather than default(JsonElement), which would be
        // JsonValueKind.Undefined and would crash System.Text.Json on serialization.
        using var emptyState = JsonDocument.Parse("{}");
        return new WorkItemDto(
            wi.Id, wi.ProjectId, wi.Title, wi.CurrentStatus, wi.CurrentCheckpointKey,
            new ExecutorRefDto(descriptor.Id, descriptor.Key, descriptor.DisplayName),
            wi.ExecutorCorrelationMarker, wi.CreatedAt,
            createdBy is null
                ? new MemberRefDto(wi.CreatedByMemberId, "(unknown)")
                : new MemberRefDto(createdBy.Id, createdBy.DisplayName),
            emptyState.RootElement.Clone(),
            wi.WorkBranch,
            wi.CurrentTaskId);
    }

    public async Task CancelAsync(Guid projectId, Guid workItemId, Guid actingMemberId, CancellationToken ct)
    {
        var wi = await db.WorkItems.FirstOrDefaultAsync(w => w.Id == workItemId && w.ProjectId == projectId, ct)
            ?? throw new NotFoundException("Work item not found.");

        var descriptor = await router.ResolveAsync(projectId, ct)
            ?? throw new ConflictException("Project has no executor bound.");

        var cancelContract = descriptor.Contracts.FirstOrDefault(c => c.CheckpointKey == CancelCheckpointKey);
        var requiredRole = cancelContract?.RequiredRoleKey ?? "operator";

        await authz.EnsureAuthorizedAsync(actingMemberId, projectId, "workitem:cancel", requiredRole, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        try
        {
            await executorClient.CancelAsync(descriptor, wi.ExecutorCorrelationMarker, ct);
        }
        catch (ExecutorFailureException ex)
        {
            await audit.WriteAsync(new AuditWriteRequest("WorkItem", wi.Id, "workitem:cancel", AuditOutcome.Failed)
            {
                ActingMemberId = actingMemberId,
                ProjectId = projectId,
                Reason = "executor failure",
                Details = ExecutorFailureDetails(ex, wi.ExecutorCorrelationMarker),
            }, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw;
        }

        await audit.WriteAsync(new AuditWriteRequest("WorkItem", wi.Id, "workitem:cancel", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            ProjectId = projectId,
            Details = new Dictionary<string, object?>
            {
                ["executorKey"] = descriptor.Key,
                ["executorCorrelationMarker"] = wi.ExecutorCorrelationMarker,
            },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await reconciler.RecomputeForWorkItemAsync(wi.Id, ct);
    }

    private async Task<Dictionary<Guid, MemberRefDto>> ResolveMembersAsync(
        IEnumerable<Guid> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, MemberRefDto>();
        foreach (var id in ids)
        {
            var m = await members.FindByIdAsync(id, ct);
            result[id] = m is null
                ? new MemberRefDto(id, "(unknown)")
                : new MemberRefDto(m.Id, m.DisplayName);
        }
        return result;
    }

    internal static IReadOnlyDictionary<string, object?> ExecutorFailureDetails(ExecutorFailureException ex, string marker)
        => new Dictionary<string, object?>
        {
            ["executorId"] = ex.ExecutorId,
            ["executorKey"] = ex.ExecutorKey,
            ["executorCorrelationMarker"] = marker,
            ["executorCorrelationId"] = ex.CorrelationId,
            ["upstreamStatus"] = ex.UpstreamStatus,
        };
}
