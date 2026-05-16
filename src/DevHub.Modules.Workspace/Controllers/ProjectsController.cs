using DevHub.Contracts;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Pagination;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.Workspace.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public sealed class ProjectsController(IProjectService svc, ICurrentMember me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] PageRequest page,
        [FromQuery] Guid? teamId,
        [FromQuery] string? projectType,
        CancellationToken ct) =>
        Ok(await svc.ListAsync(page.Normalize(), teamId, projectType, me.MemberId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(new EnvelopeDto<ProjectDto>(await svc.GetAsync(id, me.MemberId, ct)));

    [HttpGet("by-slug/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct) =>
        Ok(new EnvelopeDto<ProjectDto>(await svc.GetBySlugAsync(slug, me.MemberId, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<ProjectDto>(await svc.CreateAsync(req, me.MemberId, ct)));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProjectRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<ProjectDto>(await svc.UpdateAsync(id, req, me.MemberId, ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await svc.DeleteAsync(id, me.MemberId, ct);
        return NoContent();
    }
}
