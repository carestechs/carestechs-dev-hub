using DevHub.Contracts;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Pagination;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.Workspace.Controllers;

[ApiController]
[Route("api/members")]
[Authorize]
public sealed class MembersController(IMemberService svc, ICurrentMember me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageRequest page, [FromQuery] string? q, CancellationToken ct) =>
        Ok(await svc.ListAsync(page.Normalize(), q, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(new EnvelopeDto<MemberDto>(await svc.GetAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Invite([FromBody] InviteMemberRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<MemberDto>(await svc.InviteAsync(req, me.MemberId, ct)));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMemberRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<MemberDto>(await svc.UpdateAsync(id, req, me.MemberId, ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await svc.DeleteAsync(id, me.MemberId, ct);
        return NoContent();
    }
}
