using DevHub.Contracts;
using DevHub.Contracts.Identity;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.Workspace.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/memberships")]
[Authorize]
public sealed class MembershipsController(IMembershipService svc, ICurrentMember me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct) =>
        Ok(new EnvelopeDto<IReadOnlyList<ProjectMembershipDto>>(await svc.ListAsync(projectId, me.MemberId, ct)));

    [HttpPost]
    public async Task<IActionResult> Add(Guid projectId, [FromBody] AddMembershipRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<ProjectMembershipDto>(await svc.AddAsync(projectId, req, me.MemberId, ct)));

    [HttpPatch("{membershipId:guid}")]
    public async Task<IActionResult> Update(Guid projectId, Guid membershipId, [FromBody] UpdateMembershipRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<ProjectMembershipDto>(await svc.UpdateAsync(projectId, membershipId, req, me.MemberId, ct)));

    [HttpDelete("{membershipId:guid}")]
    public async Task<IActionResult> Remove(Guid projectId, Guid membershipId, CancellationToken ct)
    {
        await svc.RemoveAsync(projectId, membershipId, me.MemberId, ct);
        return NoContent();
    }
}
