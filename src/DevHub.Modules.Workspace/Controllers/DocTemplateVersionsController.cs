using DevHub.Contracts;
using DevHub.Contracts.Identity;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.Workspace.Controllers;

[ApiController]
[Route("api/admin/doc-templates")]
[Authorize]
public sealed class DocTemplateVersionsController(IDocTemplateVersionService svc, ICurrentMember me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(new EnvelopeDto<IReadOnlyList<DocTemplateVersionDto>>(
            await svc.ListAsync(me.MemberId, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDocTemplateVersionRequest req, CancellationToken ct)
    {
        var dto = await svc.CreateAsync(req.SourceVersionId, req.Notes, me.MemberId, ct);
        return CreatedAtAction(nameof(List), new EnvelopeDto<DocTemplateVersionDto>(dto));
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) =>
        Ok(new EnvelopeDto<DocTemplateVersionDto>(await svc.ActivateAsync(id, me.MemberId, ct)));
}
