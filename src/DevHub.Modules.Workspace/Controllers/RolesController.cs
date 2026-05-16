using DevHub.Contracts;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.Workspace.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public sealed class RolesController(IRoleService svc) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(new EnvelopeDto<IReadOnlyList<RoleDto>>(await svc.ListAsync(ct)));
}
