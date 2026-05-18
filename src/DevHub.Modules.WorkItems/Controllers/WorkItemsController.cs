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
[Route("api/projects/{projectId:guid}/work-items")]
[Authorize]
public sealed class WorkItemsController(
    IWorkItemsService svc,
    ICurrentMember me,
    IProjectAuthorizationService authz,
    IExecutorRouter router,
    WorkItemsDbContext db,
    WorkItemStreamForwarder streamForwarder) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        Guid projectId,
        [FromQuery] PageRequest page,
        [FromQuery] string? status,
        [FromQuery] bool waitingOnMe,
        CancellationToken ct) =>
        Ok(await svc.ListAsync(projectId, page.Normalize(), status, waitingOnMe, me.MemberId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid projectId, Guid id, CancellationToken ct) =>
        Ok(new EnvelopeDto<WorkItemDto>(await svc.GetAsync(projectId, id, me.MemberId, ct)));

    [HttpPost]
    public async Task<IActionResult> Start(
        Guid projectId,
        [FromBody] StartWorkItemRequest req,
        CancellationToken ct)
    {
        var dto = await svc.StartAsync(projectId, req, me.MemberId, ct);
        return CreatedAtAction(nameof(Get), new { projectId, id = dto.Id }, new EnvelopeDto<WorkItemDto>(dto));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid projectId,
        Guid id,
        [FromBody] UpdateWorkItemRequest req,
        CancellationToken ct) =>
        Ok(new EnvelopeDto<WorkItemDto>(await svc.UpdateAsync(projectId, id, req, me.MemberId, ct)));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid projectId, Guid id, CancellationToken ct)
    {
        await svc.CancelAsync(projectId, id, me.MemberId, ct);
        return NoContent();
    }

    /// <summary>
    /// SSE pass-through. Authorize ONCE here; only after the grant audit lands do we open
    /// the upstream socket. Bytes are forwarded without buffering.
    /// </summary>
    [HttpGet("{id:guid}/stream")]
    public async Task Stream(Guid projectId, Guid id, CancellationToken ct)
    {
        // Authorize first — deny path never reaches the executor (AC-1).
        await authz.EnsureAuthorizedAsync(me.MemberId, projectId, "workitem:stream", requiredRoleKey: null, ct);

        var wi = await db.WorkItems.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id && w.ProjectId == projectId, ct)
            ?? throw new NotFoundException("Work item not found.");

        var descriptor = await router.ResolveAsync(projectId, ct)
            ?? throw new ConflictException("Project has no executor bound.");

        await streamForwarder.PipeAsync(HttpContext, descriptor, new WorkItemRef(wi.ExecutorCorrelationMarker, wi.ExecutorRunId), ct);
    }
}
