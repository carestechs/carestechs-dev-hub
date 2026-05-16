using DevHub.Contracts;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Pagination;
using DevHub.Modules.WorkItems.DTOs;
using DevHub.Modules.WorkItems.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.WorkItems.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/work-items/{workItemId:guid}")]
[Authorize]
public sealed class CheckpointSignalsController(ICheckpointSignalsService svc, ICurrentMember me) : ControllerBase
{
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
