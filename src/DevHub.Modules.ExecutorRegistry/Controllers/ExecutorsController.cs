using DevHub.Contracts;
using DevHub.Contracts.Executors;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Pagination;
using DevHub.Modules.ExecutorRegistry.DTOs;
using DevHub.Modules.ExecutorRegistry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.ExecutorRegistry.Controllers;

[ApiController]
[Route("api/admin/executors")]
[Authorize]
public sealed class ExecutorsController(IExecutorRegistrationService svc, ICurrentMember me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageRequest page, [FromQuery] ExecutorStatus? status, CancellationToken ct) =>
        Ok(await svc.ListAsync(page.Normalize(), status, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(new EnvelopeDto<ExecutorDto>(await svc.GetAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateExecutorRequest req, CancellationToken ct)
    {
        var dto = await svc.CreateAsync(req, me.MemberId, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, new EnvelopeDto<ExecutorDto>(dto));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateExecutorRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<ExecutorDto>(await svc.UpdateAsync(id, req, me.MemberId, ct)));

    [HttpPost("{id:guid}/checkpoint-contracts")]
    public async Task<IActionResult> ReplaceContracts(Guid id, [FromBody] ReplaceContractsRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<ExecutorDto>(await svc.ReplaceContractsAsync(id, req, me.MemberId, ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await svc.DeleteAsync(id, me.MemberId, ct);
        return NoContent();
    }
}
