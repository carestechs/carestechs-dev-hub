# Implementation Plan: T-023 — Cross-module contracts (authorization + membership query)

## Task Reference
- **Task ID:** T-023
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** M
- **Rationale:** Centralizes the `(member, role, project, target)` check + the audit-on-grant/deny step in one service so individual controllers can't forget either.

## Overview
Publish `IProjectAuthorizationService` and `IProjectMembershipQuery` from `DevHub.Contracts` and implement both in `DevHub.Modules.Workspace`. Authorization writes audit entries from inside (callers never need to). Operator status is workspace-global: an operator passes every check without needing a `ProjectMembership`.

## Implementation Steps

### Step 1: Operator-status decision — workspace-level role assignment
**File:** `src/DevHub.Modules.Workspace/Entities/WorkspaceRoleAssignment.cs`
**Action:** Create

To represent "this member is a workspace-wide operator" cleanly, add a small entity:

```csharp
public sealed class WorkspaceRoleAssignment : BaseEntity, ISoftDeletable
{
    public required Guid MemberId { get; set; }
    public required Guid RoleId { get; set; }
    public required Guid CreatedByMemberId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
```

Plus an EF mapping in `WorkspaceDbContext` (unique on `(MemberId, RoleId)` where `DeletedAt IS NULL`).

This entity isn't in the original data-model.md, but it's the cleanest representation of "operator is workspace-scoped, everyone else is per-project." The migration adds the table; the seeder in T-005 is extended to grant `operator` role to the seed member via this table.

(Alternative considered: `Member.IsSystemOperator` boolean. Rejected because it bakes a role into the entity, and FEAT-002 explicitly wants role-based grants throughout.)

### Step 2: Authorization contract types
**Files:**
- `src/DevHub.Contracts/Authorization/AuthorizationOutcome.cs`
- `src/DevHub.Contracts/Authorization/IProjectAuthorizationService.cs`
**Action:** Create

```csharp
public sealed record AuthorizationOutcome(bool Granted, string? DeniedReason = null);

public interface IProjectAuthorizationService
{
    /// Resolves (memberId, projectId, action, requiredRoleKey?). Operator status grants
    /// workspace-wide. Writes an audit entry inside the call; callers never audit twice.
    Task<AuthorizationOutcome> AuthorizeAsync(
        Guid memberId,
        Guid projectId,
        string action,
        string? requiredRoleKey = null,
        CancellationToken ct = default);

    /// Same as AuthorizeAsync but throws ForbiddenException on Denied (convenience for controllers).
    Task EnsureAuthorizedAsync(
        Guid memberId,
        Guid projectId,
        string action,
        string? requiredRoleKey = null,
        CancellationToken ct = default);

    /// Workspace-level operator check, used by controllers for non-project-scoped admin actions
    /// (creating teams, inviting members, registering executors). Audits + throws on deny.
    Task EnsureOperatorAsync(Guid memberId, string action, CancellationToken ct = default);
}
```

### Step 3: Membership query contract
**Files:**
- `src/DevHub.Contracts/Authorization/IProjectMembershipQuery.cs`
- `src/DevHub.Contracts/Authorization/MembershipDescriptor.cs`
**Action:** Create

```csharp
public sealed record MembershipDescriptor(Guid ProjectId, string ProjectSlug, IReadOnlyList<string> Roles);

public interface IProjectMembershipQuery
{
    /// Returns the member's (project, roles) tuples. Operators get the workspace-wide operator role
    /// surfaced as an entry per project; in v1 we return [] for operators and they are special-cased
    /// at the consumer (Identity uses IsOperator separately).
    Task<IReadOnlyList<MembershipDescriptor>> GetMembershipsAsync(Guid memberId, CancellationToken ct = default);

    /// True if memberId holds the system operator role (workspace-wide).
    Task<bool> IsOperatorAsync(Guid memberId, CancellationToken ct = default);
}
```

### Step 4: Workspace implementations
**Files:**
- `src/DevHub.Modules.Workspace/Services/ProjectAuthorizationService.cs`
- `src/DevHub.Modules.Workspace/Services/ProjectMembershipQuery.cs`
**Action:** Create

`ProjectAuthorizationService`:
1. Resolve `IsOperator` via `_db.WorkspaceRoleAssignments.AnyAsync(w => w.MemberId == memberId && w.Role.Key == "operator")` (cached per request via a private `Lazy<Task<bool>>` field on the scoped service).
2. If operator → `Granted`. Write audit entry with `Outcome = Granted`, `Reason = "operator"`. Return.
3. Look up `ProjectMembership` for `(memberId, projectId)`. If missing → `Denied("not a member of this project")`. Audit + return.
4. If `requiredRoleKey is null` → `Granted` (any-role read). Audit + return.
5. Look up roles on that membership. If `requiredRoleKey` not in the set → `Denied($"member lacks role '{requiredRoleKey}'")`. Audit + return.
6. Otherwise → `Granted`. Audit + return.

`AuditWriteRequest` includes `Details = { check: { requiredRoleKey, isOperator, projectId } }` on deny so the operator dashboard can diagnose.

`EnsureAuthorizedAsync` calls `AuthorizeAsync` and throws `ForbiddenException(outcome.DeniedReason!)` on deny.

`EnsureOperatorAsync` is the workspace-level variant — same shape but no project lookup.

`ProjectMembershipQuery` joins `ProjectMembership` × `RoleAssignment` × `Project` and projects into `MembershipDescriptor`. `IsOperatorAsync` is the WorkspaceRoleAssignment lookup above.

### Step 5: Seeder extension — seed operator gets workspace operator role
**File:** `src/DevHub.Modules.Workspace/Seeding/WorkspaceSeeder.cs`
**Action:** Modify

After ensuring the operator role + seed member exist, also insert a `WorkspaceRoleAssignment` if missing (`MemberId = seed member, RoleId = operator role, CreatedByMemberId = seed member`).

### Step 6: Migration
**Action:** Generate
`dotnet ef migrations add WorkspaceRoleAssignment --project src/DevHub.Modules.Workspace --startup-project src/DevHub.Api --context WorkspaceDbContext`. Inspect: should add `workspace.workspace_role_assignments` with the unique partial index.

### Step 7: Wire DI
**File:** `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs`
**Action:** Modify

```csharp
services.AddScoped<IProjectAuthorizationService, ProjectAuthorizationService>();
services.AddScoped<IProjectMembershipQuery, ProjectMembershipQuery>();
```

### Step 8: Tests
**File:** `tests/DevHub.Modules.Workspace.Tests/ProjectAuthorizationServiceTests.cs`
**Action:** Create

Five integration tests under `[Collection("postgres")]`:
1. Operator → `Granted`; audit row written with `Reason = "operator"`.
2. Non-member → `Denied("not a member...")`; audit row written.
3. Member without required role → `Denied("...lacks role 'reviewer'")`; audit row.
4. Member with required role → `Granted`; audit row.
5. Member, `requiredRoleKey is null` (read-any) → `Granted`; audit row.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `src/DevHub.Modules.Workspace/Entities/WorkspaceRoleAssignment.cs` | Create | Workspace-level role grant entity |
| `src/DevHub.Modules.Workspace/WorkspaceDbContext.cs` | Modify | DbSet + mapping + unique index |
| `src/DevHub.Modules.Workspace/Migrations/*WorkspaceRoleAssignment*.cs` | Create | New migration |
| `src/DevHub.Contracts/Authorization/*.cs` | Create | Outcome, interface, query, descriptor |
| `src/DevHub.Modules.Workspace/Services/ProjectAuthorizationService.cs` | Create | Implementation |
| `src/DevHub.Modules.Workspace/Services/ProjectMembershipQuery.cs` | Create | Implementation |
| `src/DevHub.Modules.Workspace/Seeding/WorkspaceSeeder.cs` | Modify | Seed operator's workspace role assignment |
| `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs` | Modify | Register both services |
| `tests/DevHub.Modules.Workspace.Tests/ProjectAuthorizationServiceTests.cs` | Create | 5 integration tests |

## Edge Cases & Risks
- **Operator promoted mid-request** — the per-request cache means a member promoted to operator during a long request stays at their pre-call status until the next request. Acceptable.
- **Soft-deleted membership returning `Denied` reads as "not a member"** — correct behavior; the response is identical to never having been a member.
- **Audit-on-every-call cost** — each check is one extra INSERT. v1 is fine; if it becomes a hot path, an out-of-band batching writer is the natural follow-up (out of scope here).

## Acceptance Verification
- [ ] `IProjectAuthorizationService.AuthorizeAsync` returns `Granted` for operator; audit row written.
- [ ] Non-member receives `Denied("not a member...")`; audit row written.
- [ ] Wrong role receives `Denied("...lacks role 'X'")`; audit row.
- [ ] Right role / null requiredRoleKey returns `Granted`; audit row.
- [ ] `IProjectMembershipQuery.GetMembershipsAsync` returns `[]` for a member with no memberships.
- [ ] All 5 integration tests pass under `dotnet test`.
