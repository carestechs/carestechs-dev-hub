using DevHub.Contracts;
using DevHub.Contracts.Identity;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.Workspace.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/docs")]
[Authorize]
public sealed class ProjectDocsController(IProjectDocsService svc, ICurrentMember me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct) =>
        Ok(new EnvelopeDto<IReadOnlyList<ProjectDocSummaryDto>>(
            await svc.ListAsync(projectId, me.MemberId, ct)));

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(Guid projectId, string key, CancellationToken ct) =>
        Ok(new EnvelopeDto<ProjectDocDto>(await svc.GetAsync(projectId, key, me.MemberId, ct)));

    [HttpPut("{key}")]
    public async Task<IActionResult> Upsert(
        Guid projectId, string key, [FromBody] UpsertDocRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<ProjectDocDto>(
            await svc.UpsertAsync(projectId, key, req.Content, me.MemberId, ct)));
}
