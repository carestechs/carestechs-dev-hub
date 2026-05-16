using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Pagination;
using DevHub.Modules.Audit.DTOs;
using DevHub.Modules.Audit.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.Audit.Controllers;

[ApiController]
[Authorize]
public sealed class AuditController(
    IAuditQueryService query,
    IProjectAuthorizationService authz,
    ICurrentMember me) : ControllerBase
{
    private const int MaxPageSize = 200;

    [HttpGet("api/projects/{projectId:guid}/audit")]
    public async Task<IActionResult> Project(
        Guid projectId,
        [FromQuery] PageRequest page,
        [FromQuery] AuditFilterQuery q,
        CancellationToken ct)
    {
        await authz.EnsureAuthorizedAsync(me.MemberId, projectId, "audit:read", requiredRoleKey: null, ct);
        return Ok(await query.ListForProjectAsync(projectId, q.ToFilter(), Clamp(page.Normalize()), ct));
    }

    [HttpGet("api/admin/audit")]
    public async Task<IActionResult> Admin(
        [FromQuery] PageRequest page,
        [FromQuery] AuditFilterQuery q,
        CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(me.MemberId, "audit:read:admin", ct);
        return Ok(await query.ListAsync(q.ToFilter(), Clamp(page.Normalize()), ct));
    }

    private static PageRequest Clamp(PageRequest p) =>
        p.PageSize > MaxPageSize ? p with { PageSize = MaxPageSize } : p;
}

/// Query-string projection of <see cref="AuditFilter"/>. ASP.NET binds enums by name (case-insensitive
/// via the JSON enum converter wired in Program.cs).
public sealed class AuditFilterQuery
{
    public Guid? ActingMemberId { get; init; }
    public string? TargetType { get; init; }
    public string? Action { get; init; }
    public AuditOutcome? Outcome { get; init; }
    public Guid? ProjectId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    public AuditFilter ToFilter() => new()
    {
        ActingMemberId = ActingMemberId,
        TargetType = TargetType,
        Action = Action,
        Outcome = Outcome,
        ProjectId = ProjectId,
        From = From,
        To = To,
    };
}
