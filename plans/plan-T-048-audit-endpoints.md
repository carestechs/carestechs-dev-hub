# Implementation Plan: T-048 — Audit endpoints + integration tests

## Task Reference
- **Task ID:** T-048 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** Lights up the audit read API. AC-3 dashboards depend on the admin endpoint; AC-4 scoping (`Project:any` vs `System:operator`) is enforced here.

## Overview
Thin `AuditController` with two actions backed by `IAuditQueryService`. Integration tests cover filter correctness, scoping, pagination, and the soft-deleted-project preservation rule.

## Implementation Steps

### Step 1: Controller
**File:** `src/DevHub.Modules.Audit/Controllers/AuditController.cs` · Create

```csharp
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
        var pageReq = Normalize(page);
        return Ok(await query.ListForProjectAsync(projectId, q.ToFilter(), pageReq, ct));
    }

    [HttpGet("api/admin/audit")]
    public async Task<IActionResult> Admin(
        [FromQuery] PageRequest page,
        [FromQuery] AuditFilterQuery q,
        CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(me.MemberId, "audit:read:admin", ct);
        var pageReq = Normalize(page);
        return Ok(await query.ListAsync(q.ToFilter(), pageReq, ct));
    }

    private static PageRequest Normalize(PageRequest req)
    {
        var p = req.Normalize();
        if (p.PageSize > MaxPageSize) p = p with { PageSize = MaxPageSize };
        return p;
    }
}

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
        ActingMemberId = ActingMemberId, TargetType = TargetType, Action = Action,
        Outcome = Outcome, ProjectId = ProjectId, From = From, To = To,
    };
}
```

### Step 2: csproj
**File:** `src/DevHub.Modules.Audit/DevHub.Modules.Audit.csproj` · Modify
Ensure `<FrameworkReference Include="Microsoft.AspNetCore.App" />` is present (drop redundant Configuration/Hosting abstractions packages if NU1510 triggers).

### Step 3: Verify wiring
**File:** `src/DevHub.Api/Program.cs` · Verify
`AddApplicationPart(typeof(AuditDbContext).Assembly)` was added at T-024.

### Step 4: Integration tests
**File:** `tests/DevHub.Modules.Audit.Tests/AuditEndpointsTests.cs` · Create

Tests (`[Collection("postgres")]`, `UseFakeExecutor = true`):
- `Project_audit_as_member_returns_envelope_with_meta` — operator creates project + team, calls `GET /api/projects/{}/audit`, asserts at least one entry returned.
- `Project_audit_as_non_member_returns_403_and_audits_denied` — fresh non-member → 403 + a Denied row in admin audit.
- `Admin_audit_as_operator_returns_envelope` — operator hits `/api/admin/audit`, sees cross-project rows.
- `Admin_audit_as_non_operator_returns_403_and_audits_denied`.
- `Admin_audit_filter_by_outcome_denied_returns_only_denied` — sanity check on filter.
- `Admin_audit_filter_by_action_returns_only_matching` — e.g., `action=team:create`.
- `Admin_audit_filter_by_acting_member_returns_only_that_member`.
- `Admin_audit_filter_by_from_and_to_returns_range_only`.
- `Project_audit_preserves_history_after_project_soft_delete` — create project, generate audit rows, soft-delete the project, re-fetch `/api/admin/audit?projectId={}` — rows still appear.
- `Page_size_clamped_to_200_when_caller_requests_10000`.

## Files Affected
| File | Action |
|------|--------|
| `Audit/Controllers/AuditController.cs` | Create |
| `Audit/DevHub.Modules.Audit.csproj` | Modify (FrameworkReference) |
| `Audit.Tests/AuditEndpointsTests.cs` | Create |

## Edge Cases & Risks
- **Self-referencing audit on the audit-read deny path.** When a non-member calls `/projects/{}/audit`, `EnsureAuthorizedAsync` writes a Denied row with `Action = "audit:read"`. That row is then visible via `/admin/audit` to operators — which is exactly the intended visibility. No infinite recursion: the deny path doesn't itself audit-read.
- **Soft-deleted projects.** The project_id reference on `AuditEntry` is a raw column, not an FK with a soft-delete filter. Queries return all rows regardless of project status.
- **Filter parsing.** `AuditOutcome?` from query string: ASP.NET model binding handles enums by name. Verify case sensitivity in the integration test.

## Acceptance Verification
- [ ] `dotnet build` clean.
- [ ] `AuditEndpointsTests` is green with ≥10 cases.
- [ ] Existing tests stay green.
