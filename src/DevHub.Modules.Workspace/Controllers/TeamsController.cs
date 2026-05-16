using DevHub.Contracts;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Pagination;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.Workspace.Controllers;

[ApiController]
[Route("api/teams")]
[Authorize]
public sealed class TeamsController(ITeamService svc, ICurrentMember me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageRequest page, CancellationToken ct) =>
        Ok(await svc.ListAsync(page.Normalize(), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(new EnvelopeDto<TeamDto>(await svc.GetAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTeamRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<TeamDto>(await svc.CreateAsync(req, me.MemberId, ct)));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTeamRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<TeamDto>(await svc.UpdateAsync(id, req, me.MemberId, ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await svc.DeleteAsync(id, me.MemberId, ct);
        return NoContent();
    }
}
