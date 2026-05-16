using DevHub.Contracts;
using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Authorization;
using DevHub.Contracts.Executors;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Pagination;
using DevHub.Modules.WorkItems.DTOs;
using DevHub.Modules.WorkItems.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.WorkItems.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/work-items/{workItemId:guid}")]
[Authorize]
public sealed class CheckpointSignalsController(
    ICheckpointSignalsService svc,
    ICurrentMember me,
    IProjectAuthorizationService authz,
    IExecutorRouter router,
    WorkItemsDbContext db) : ControllerBase
{
    [HttpGet("checkpoints/{checkpointKey}")]
    public async Task<IActionResult> GetCheckpoint(
        Guid projectId,
        Guid workItemId,
        string checkpointKey,
        CancellationToken ct)
    {
        await authz.EnsureAuthorizedAsync(me.MemberId, projectId, "checkpoint:read", requiredRoleKey: null, ct);

        var wi = await db.WorkItems.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workItemId && w.ProjectId == projectId, ct)
            ?? throw new NotFoundException("Work item not found.");

        var descriptor = await router.ResolveAsync(projectId, ct)
            ?? throw new ConflictException("Project has no executor bound.");

        var contract = await router.GetCheckpointContractAsync(descriptor.Id, checkpointKey, ct)
            ?? throw new NotFoundException($"Checkpoint '{checkpointKey}' is not defined on this executor.");

        return Ok(new EnvelopeDto<CheckpointContractView>(new CheckpointContractView(
            contract.CheckpointKey, contract.DisplayName, contract.RequiredRoleKey, contract.AllowedOutcomes,
            wi.CurrentCheckpointKey == checkpointKey ? "active" : "not-active")));
    }

    [HttpPost("checkpoints/{checkpointKey}/signal")]
    public async Task<IActionResult> Signal(
        Guid projectId,
        Guid workItemId,
        string checkpointKey,
        [FromBody] SignalRequest req,
        CancellationToken ct)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrEmpty(idempotencyKey)) idempotencyKey = null;

        var dto = await svc.SignalAsync(projectId, workItemId, checkpointKey, req, idempotencyKey, me.MemberId, ct);
        return Ok(new EnvelopeDto<WorkItemDto>(dto));
    }

    [HttpGet("signals")]
    public async Task<IActionResult> List(
        Guid projectId,
        Guid workItemId,
        [FromQuery] PageRequest page,
        CancellationToken ct) =>
        Ok(await svc.ListSignalsAsync(projectId, workItemId, page.Normalize(), me.MemberId, ct));
}

public sealed record CheckpointContractView(
    string CheckpointKey,
    string DisplayName,
    string RequiredRoleKey,
    IReadOnlyList<string> AllowedOutcomes,
    string State);
