using DevHub.Contracts;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Pagination;
using DevHub.Modules.WorkItems.DTOs;
using DevHub.Modules.WorkItems.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.WorkItems.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/work-items")]
[Authorize]
public sealed class WorkItemsController(IWorkItemsService svc, ICurrentMember me) : ControllerBase
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

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid projectId, Guid id, CancellationToken ct)
    {
        await svc.CancelAsync(projectId, id, me.MemberId, ct);
        return NoContent();
    }
}
