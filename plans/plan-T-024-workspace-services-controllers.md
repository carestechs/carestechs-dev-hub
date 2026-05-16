# Implementation Plan: T-024 — Workspace services + controllers

## Task Reference
- **Task ID:** T-024
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** XL
- **Rationale:** This is the bulk of FEAT-002 — every endpoint in api-spec.md §Workspace, plus the wiring that makes Success Metric #1 demonstrable.

## Overview
Ship `TeamService` / `MemberService` / `ProjectService` / `MembershipService` / `RoleService`, their thin controllers, and the JWT-role refactor in `AuthenticationService`. Every mutation: authorize → mutate → audit, in one transaction. Every list: paginated envelope. Identity's `/api/auth/me` now returns the member's real memberships.

## Implementation Steps

### Step 1: Pagination contract
**Files:**
- `src/DevHub.Contracts/Pagination/PageRequest.cs`
- `src/DevHub.Contracts/Pagination/PageMeta.cs`
- `src/DevHub.Contracts/Pagination/PagedEnvelopeDto.cs`
**Action:** Create

```csharp
public sealed record PageRequest(int Page = 1, int PageSize = 20, string? SortBy = null, string? SortDir = null);
public sealed record PageMeta(int TotalCount, int Page, int PageSize, string? SortBy, string? SortDir);
public sealed record PagedEnvelopeDto<T>(IReadOnlyList<T> Data, PageMeta Meta);
```

`PageRequest` clamps to 1..100 in a `Normalize()` extension; controllers call `req.Normalize()` before passing to the service.

### Step 2: DTOs
**File:** `src/DevHub.Modules.Workspace/DTOs/*.cs`
**Action:** Create
- `TeamDto(Guid Id, string Name, string? Description, int ProjectCount, DateTimeOffset CreatedAt)`
- `CreateTeamRequest`, `UpdateTeamRequest` with `[Required]`, `[MaxLength(120)]`
- `MemberDto(Guid Id, string DisplayName, string Email, string Status, DateTimeOffset CreatedAt)`
- `InviteMemberRequest(string DisplayName, string Email)`
- `UpdateMemberRequest(string? DisplayName, MemberStatus? Status)`
- `ProjectDto(Guid Id, string Name, string Slug, string ProjectType, TeamRefDto OwningTeam, string? Description, int InFlightWorkItems, DateTimeOffset CreatedAt)` with `TeamRefDto(Guid Id, string Name)`
- `CreateProjectRequest`, `UpdateProjectRequest`
- `ProjectMembershipDto(Guid Id, MemberRefDto Member, IReadOnlyList<string> Roles, DateTimeOffset CreatedAt)` with `MemberRefDto(Guid Id, string DisplayName, string Email)`
- `AddMembershipRequest(Guid MemberId, IReadOnlyList<string> RoleKeys)`, `UpdateMembershipRequest(IReadOnlyList<string> RoleKeys)`
- `RoleDto(Guid Id, string Key, string Name, string? Description, bool IsSystem)`

All wrapped in `EnvelopeDto<T>` (single) or `PagedEnvelopeDto<T>` (list).

### Step 3: Services
**Files:** `src/DevHub.Modules.Workspace/Services/{Team,Member,Project,Membership,Role}Service.cs`
**Action:** Create

Pattern for every mutation method:
```csharp
public async Task<TDto> CreateAsync(TRequest req, Guid actingMemberId, CancellationToken ct)
{
    // 1. Authorize (writes audit on grant/deny inside).
    await _authz.EnsureOperatorAsync(actingMemberId, "team:create", ct);

    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    // 2. Validate (uniqueness, FK existence) — throws DomainException on conflict.
    if (await _db.Teams.AnyAsync(t => t.Name == req.Name, ct))
        throw new ConflictException($"Team '{req.Name}' already exists.");

    // 3. Mutate.
    var team = new Team { Name = req.Name, Description = req.Description };
    _db.Teams.Add(team);

    // 4. Audit success.
    await _audit.WriteAsync(new AuditWriteRequest("Team", team.Id, "team:create", AuditOutcome.Granted)
    {
        ActingMemberId = actingMemberId,
        Details = new Dictionary<string, object?> { ["name"] = req.Name },
    }, ct);

    // 5. Commit both inserts atomically.
    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return Map(team);
}
```

Key service rules:
- **TeamService.Delete**: throw `ConflictException` if the team owns any non-deleted projects. Soft delete only.
- **ProjectService.Create**: validate `OwningTeamId` exists, validate `Slug` regex `^[a-z0-9-]+$`, accept any non-empty `ProjectType` (T-024 logs a TODO for FEAT-003's `ExecutorBinding` validation).
- **ProjectService.Delete**: soft delete + soft-cascade to `ProjectMembership`/`RoleAssignment` in one transaction.
- **MembershipService.Add**: project-scoped authorization (`EnsureAuthorizedAsync(actingMember, projectId, "membership:add")`) with operator-only requirement embedded by passing `requiredRoleKey: null` + a workspace-operator gate above. For v1 simplicity, ALL membership writes require operator role; project-scoped role-based membership editing lands in v2.
- **MembershipService.Remove**: if the membership being removed is the last operator across the workspace, throw `ConflictException("at least one operator must remain")`.
- **RoleService**: read-only in v1 (`GET /api/roles`). Role assignments happen via `MembershipService.UpdateRoles`.

### Step 4: Controllers
**Files:** `src/DevHub.Modules.Workspace/Controllers/{Teams,Members,Projects,Memberships,Roles}Controller.cs`
**Action:** Create

Each controller is thin:
```csharp
[ApiController, Route("api/teams")]
[Authorize]
public sealed class TeamsController(ITeamService svc, ICurrentMember me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageRequest page, CancellationToken ct) =>
        Ok(new EnvelopeDto<PagedEnvelopeDto<TeamDto>>(await svc.ListAsync(page.Normalize(), ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTeamRequest req, CancellationToken ct) =>
        Ok(new EnvelopeDto<TeamDto>(await svc.CreateAsync(req, me.MemberId, ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(new EnvelopeDto<TeamDto>(await svc.GetAsync(id, ct)));

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
```

Same shape for Members, Projects, Memberships (nested under `/api/projects/{projectId}/memberships`), Roles (read-only).

### Step 5: Identity refactor — JWT carries real roles
**File:** `src/DevHub.Modules.Identity/Services/AuthenticationService.cs`
**Action:** Modify

Replace the stubbed `GetRoleKeysAsync` (currently returns `[ "operator" ]` for everyone) with:

```csharp
private async Task<List<string>> GetRoleKeysAsync(Guid memberId, CancellationToken ct)
{
    var memberships = await _memberships.GetMembershipsAsync(memberId, ct);
    var roles = memberships.SelectMany(m => m.Roles).ToHashSet();
    if (await _memberships.IsOperatorAsync(memberId, ct)) roles.Add("operator");
    return roles.ToList();
}
```

Inject `IProjectMembershipQuery` into `AuthenticationService` (already wired through Contracts).

`GetCurrentMemberAsync` now also calls `_memberships.GetMembershipsAsync` so `/api/auth/me` returns real `MembershipDto`s.

### Step 6: Wire DI
**File:** `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs`
**Action:** Modify

```csharp
services.AddScoped<ITeamService, TeamService>();
services.AddScoped<IMemberService, MemberService>();
services.AddScoped<IProjectService, ProjectService>();
services.AddScoped<IMembershipService, MembershipService>();
services.AddScoped<IRoleService, RoleService>();
```

### Step 7: Smoke
**Action:** Verify
- `dotnet build` clean.
- Boot API, hit `POST /api/teams` as the seed operator → 200 with the new team.
- Hit `POST /api/teams` as an unauthenticated request → 401.
- Hit `POST /api/auth/me` after seeding a membership → memberships array now non-empty.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `src/DevHub.Contracts/Pagination/*.cs` | Create | `PageRequest`, `PageMeta`, `PagedEnvelopeDto` |
| `src/DevHub.Modules.Workspace/DTOs/*.cs` | Create | 16+ DTOs + request types |
| `src/DevHub.Modules.Workspace/Services/*Service.cs` | Create | 5 service classes |
| `src/DevHub.Modules.Workspace/Controllers/*Controller.cs` | Create | 5 thin controllers |
| `src/DevHub.Modules.Identity/Services/AuthenticationService.cs` | Modify | Real role lookup |
| `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs` | Modify | DI |

## Edge Cases & Risks
- **`/api/projects/{id}/memberships` ID resolution** — controller accepts either a Guid or a slug. Internally normalize to Guid via `IProjectLookup`. Defer the slug-route variant to a focused follow-up if it spills here.
- **Unknown projectType** — until FEAT-003 lands the `ExecutorBinding`, projects accept any non-empty string. Log `LogWarning("project_type {Type} has no executor binding yet (FEAT-003 will validate)")` so this gap is visible.
- **Audit-write divergence** — Step 3's pattern requires the same `BeginTransactionAsync` + `WriteAsync` + `SaveChangesAsync` + `CommitAsync` ordering everywhere. T-025's per-controller audit-presence tests catch any drift.
- **JWT role explosion** — a member with 50 projects could have 200 role keys in the JWT. Keep the JWT under ~4 KB; if it becomes an issue, switch to a single `mid` claim and resolve roles per-request from cache (out of scope here).

## Acceptance Verification
- [ ] Operator end-to-end: create team → invite member → create project → add membership with roles → REST-only.
- [ ] Non-operator project-scoped 403 with audit row.
- [ ] Team delete with owned projects → 409.
- [ ] Project delete soft-cascades memberships.
- [ ] Duplicate membership → 409.
- [ ] Last-operator-removal → 409.
- [ ] All list endpoints return `{ data: T[], meta: PageMeta }`.
- [ ] Every mutation writes one audit row inside the same transaction (T-025 asserts).
