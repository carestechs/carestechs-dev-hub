using DevHub.Contracts;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Pagination;
using DevHub.Modules.ExecutorRegistry.DTOs;
using DevHub.Modules.ExecutorRegistry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.ExecutorRegistry.Controllers;

[ApiController]
[Route("api/admin/executor-bindings")]
[Authorize]
public sealed class ExecutorBindingsController(IExecutorBindingService svc, ICurrentMember me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageRequest page, CancellationToken ct) =>
        Ok(await svc.ListAsync(page.Normalize(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBindingRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<ExecutorBindingDto>(await svc.CreateAsync(req, me.MemberId, ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await svc.DeleteAsync(id, me.MemberId, ct);
        return NoContent();
    }
}
