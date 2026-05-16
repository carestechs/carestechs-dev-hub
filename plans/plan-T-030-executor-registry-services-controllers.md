# Implementation Plan: T-030 — Executor Registry services + controllers + IExecutorRouter + Project hook

## Task Reference
- **Task ID:** T-030
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** XL
- **Rationale:** FEAT-003 AC-1..AC-5 + edge cases. Lands the operator surface and the router contract every later FEAT consumes.

## Overview
Five deliverables in one PR (they share the auth-extension + audit-pattern + DI wiring):
1. Cross-module contracts: `IExecutorRouter`, `IExecutorCredentialResolver`, descriptors.
2. Auth-service extension: `AuthorizeWorkspaceOperatorAsync` (workspace-scoped twin of the existing project authorize).
3. Module services: `ExecutorRegistrationService`, `ExecutorBindingService`, `ExecutorRouter`, `ExecutorCredentialResolver`.
4. Controllers: `ExecutorsController`, `ExecutorBindingsController`.
5. Workspace integration: `ProjectService.Create` now calls `IExecutorRouter.IsProjectTypeBoundAsync` (replaces the FEAT-002 TODO).

## Implementation Steps

### Step 1: Published contracts
**Files (Create):**
- `src/DevHub.Contracts/Executors/ExecutorRegistrationDescriptor.cs`
- `src/DevHub.Contracts/Executors/CheckpointContractDescriptor.cs`
- `src/DevHub.Contracts/Executors/IExecutorRouter.cs`
- `src/DevHub.Contracts/Executors/IExecutorCredentialResolver.cs`

```csharp
public sealed record ExecutorRegistrationDescriptor(
    Guid Id, string Key, string DisplayName, string BaseUrl,
    ExecutorStatus Status, IReadOnlyList<CheckpointContractDescriptor> Contracts);

public sealed record CheckpointContractDescriptor(
    string CheckpointKey, string DisplayName, string RequiredRoleKey,
    IReadOnlyList<string> AllowedOutcomes);

public interface IExecutorRouter
{
    Task<ExecutorRegistrationDescriptor?> ResolveAsync(Guid projectId, CancellationToken ct = default);
    Task<bool> IsProjectTypeBoundAsync(string projectType, CancellationToken ct = default);
    Task<CheckpointContractDescriptor?> GetCheckpointContractAsync(Guid executorId, string checkpointKey, CancellationToken ct = default);
}

public interface IExecutorCredentialResolver
{
    Task<string?> ResolveAsync(Guid executorId, CancellationToken ct = default);
}
```

`ExecutorStatus` is duplicated in `DevHub.Contracts/Executors/ExecutorStatus.cs` (same shape as the module enum). Module entity uses the Contracts enum to avoid a public-API duplicate.

### Step 2: Workspace auth-service extension
**Files (Modify):**
- `src/DevHub.Contracts/Authorization/IProjectAuthorizationService.cs` — add:
```csharp
Task<AuthorizationOutcome> AuthorizeWorkspaceOperatorAsync(
    Guid actingMemberId, string action,
    string targetType, Guid? targetId = null,
    IReadOnlyDictionary<string, object?>? details = null,
    CancellationToken ct = default);
```
- `src/DevHub.Modules.Workspace/Services/ProjectAuthorizationService.cs` — implement: check `member.IsOperator`, write audit (`Granted` or `Denied`), return outcome.

Mirror the existing project-scoped method; the difference is `projectId = null` on the audit row and no membership/role-key checks.

### Step 3: ExecutorRouter + CredentialResolver
**File:** `src/DevHub.Modules.ExecutorRegistry/Services/ExecutorRouter.cs` · Create

```csharp
internal sealed class ExecutorRouter(ExecutorRegistryDbContext db, IProjectLookup projects) : IExecutorRouter
{
    public async Task<bool> IsProjectTypeBoundAsync(string projectType, CancellationToken ct = default) =>
        await db.Bindings.AnyAsync(b => b.ProjectType == projectType && b.DeletedAt == null, ct);

    public async Task<ExecutorRegistrationDescriptor?> ResolveAsync(Guid projectId, CancellationToken ct = default)
    {
        var projectType = await projects.GetProjectTypeAsync(projectId, ct);
        if (projectType is null) return null;
        var binding = await db.Bindings.Where(b => b.ProjectType == projectType && b.DeletedAt == null).FirstOrDefaultAsync(ct);
        if (binding is null) return null;
        var exec = await db.Executors
            .Include(e => e.CheckpointContracts)
            .FirstOrDefaultAsync(e => e.Id == binding.ExecutorId && e.DeletedAt == null, ct);
        return exec is null ? null : Map(exec);
    }

    public async Task<CheckpointContractDescriptor?> GetCheckpointContractAsync(Guid executorId, string key, CancellationToken ct = default) =>
        Map(await db.CheckpointContracts.FirstOrDefaultAsync(c => c.ExecutorId == executorId && c.CheckpointKey == key, ct));
}
```

`IProjectLookup` is a new tiny contract in `DevHub.Contracts/Identity` (`Task<string?> GetProjectTypeAsync(Guid projectId, CancellationToken ct)`) implemented in Workspace. Avoids ExecutorRegistry depending on Workspace's DbContext. Alternative: skip the lookup and force callers to pass `projectType` — but the FEAT-004 façade only knows `projectId`, so the lookup is the cleaner contract.

**File:** `src/DevHub.Modules.ExecutorRegistry/Services/ExecutorCredentialResolver.cs` · Create

```csharp
internal sealed class ExecutorCredentialResolver(ExecutorRegistryDbContext db) : IExecutorCredentialResolver
{
    public async Task<string?> ResolveAsync(Guid executorId, CancellationToken ct = default)
    {
        var refName = await db.Executors
            .Where(e => e.Id == executorId && e.DeletedAt == null)
            .Select(e => e.CredentialsRef).FirstOrDefaultAsync(ct);
        return refName is null ? null : Environment.GetEnvironmentVariable(refName);
    }
}
```

**Never log the resolved value.** Caller is responsible for using it transiently.

### Step 4: Module services
**Files (Create):**
- `src/DevHub.Modules.ExecutorRegistry/Services/ExecutorRegistrationService.cs`
- `src/DevHub.Modules.ExecutorRegistry/Services/ExecutorBindingService.cs`

Pattern per service method (mirror T-024's services):
```csharp
// 1. authorize
var outcome = await _authz.AuthorizeWorkspaceOperatorAsync(currentMember, "executor:create", "ExecutorRegistration", null, details, ct);
if (!outcome.Granted) throw new ForbiddenException(outcome.DeniedReason ?? "Forbidden");

// 2. open tx
using var tx = await _db.Database.BeginTransactionAsync(ct);

// 3. validate (e.g. requiredRoleKey existence — batched lookup against Workspace via IRoleLookup)
var unknownRoles = await ValidateRequiredRoleKeysAsync(request.CheckpointContracts.Select(c => c.RequiredRoleKey), ct);
if (unknownRoles.Count > 0) throw new ValidationException($"Unknown role keys: {string.Join(",", unknownRoles)}");

// 4. mutate
var entry = new ExecutorRegistration { ... };
_db.Executors.Add(entry);

// 5. audit (Granted) inside same tx
await _audit.WriteAsync(new AuditWriteRequest("ExecutorRegistration", entry.Id, "executor:create", AuditOutcome.Granted) { ... }, ct);

// 6. save + commit
await _db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
return MapDto(entry);
```

Key behaviors:
- `Create`: validates `requiredRoleKey` for every contract via a new `IRoleLookup.ExistsAsync(string key)` published from Contracts (Workspace implements). 409 on duplicate `key`.
- `Update` (PATCH): validates status transitions are all allowed (any → any). 404 if id not found / soft-deleted.
- `ReplaceContracts`: single tx — `ExecuteDeleteAsync` then `AddRange` then save.
- `Delete`: refuses (409) if any non-deleted `ExecutorBinding` references it; otherwise sets `DeletedAt = UtcNow`.
- `ListAsync`: paginated, default `sortBy=createdAt desc`.
- Binding `Create`: 409 on duplicate active `projectType`; 404 if executor doesn't exist or is soft-deleted.
- Binding `Delete`: idempotent — `404` if already deleted.

DTO mapping happens in the service (per CLAUDE.md). `ExecutorDto` echoes `credentialsRef` as a literal (it's a *reference*, not the secret).

### Step 5: Controllers
**Files (Create):**
- `src/DevHub.Modules.ExecutorRegistry/Controllers/ExecutorsController.cs` — `[Authorize] [Route("api/admin/executors")]`
- `src/DevHub.Modules.ExecutorRegistry/Controllers/ExecutorBindingsController.cs` — `[Authorize] [Route("api/admin/executor-bindings")]`

Thin: parse → call service → wrap in `{ data, meta? }` envelope. Service throws `ForbiddenException` / `NotFoundException` / `ConflictException` / `ValidationException` — the global problem-detail handler from T-007 translates.

### Step 6: ProjectService hook
**File:** `src/DevHub.Modules.Workspace/Services/ProjectService.cs` · Modify
Replace the FEAT-002 TODO log line with:
```csharp
if (!await _router.IsProjectTypeBoundAsync(request.ProjectType, ct))
    throw new ConflictException("no executor bound for this project type");
```
Add `IExecutorRouter` to the service ctor. Run before opening the transaction (cheap read, fast failure path).

### Step 7: DI wiring
**File:** `src/DevHub.Modules.ExecutorRegistry/ExecutorRegistryModuleExtensions.cs` · Modify
```csharp
services.AddScoped<IExecutorRouter, ExecutorRouter>();
services.AddScoped<IExecutorCredentialResolver, ExecutorCredentialResolver>();
services.AddScoped<ExecutorRegistrationService>();
services.AddScoped<ExecutorBindingService>();
```

**File:** `src/DevHub.Api/Program.cs` · Verify
The `AddApplicationPart(typeof(ExecutorsController).Assembly)` line was added in T-024; confirm both new controllers are discovered.

**File:** `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs` · Verify
`ProjectService` ctor now takes `IExecutorRouter`; DI will resolve from ExecutorRegistry's registration. Order in `Program.cs`: register ExecutorRegistry *before* Workspace.

## Files Affected
| File | Action |
|------|--------|
| `Contracts/Executors/*.cs` | Create (4 files) |
| `Contracts/Authorization/IProjectAuthorizationService.cs` | Modify |
| `Contracts/Identity/IProjectLookup.cs`, `IRoleLookup.cs` | Create |
| `Workspace/Services/ProjectAuthorizationService.cs` | Modify (add workspace-operator method) |
| `Workspace/Services/ProjectService.cs` | Modify (router hook) |
| `Workspace/Services/ProjectLookup.cs`, `RoleLookup.cs` | Create |
| `ExecutorRegistry/DTOs/*.cs` | Create (~7) |
| `ExecutorRegistry/Services/{ExecutorRegistration,ExecutorBinding}Service.cs` | Create |
| `ExecutorRegistry/Services/ExecutorRouter.cs`, `ExecutorCredentialResolver.cs` | Create |
| `ExecutorRegistry/Controllers/{Executors,ExecutorBindings}Controller.cs` | Create |
| `ExecutorRegistry/ExecutorRegistryModuleExtensions.cs` | Modify |
| `Workspace/WorkspaceModuleExtensions.cs` | Modify (register lookups) |

## Edge Cases & Risks
- **DI cycle**: ExecutorRegistry depends on `IRoleLookup`/`IProjectLookup` (Workspace), Workspace depends on `IExecutorRouter` (ExecutorRegistry). Resolves because both depend on contracts only and `IExecutorRouter`/`IRoleLookup`/`IProjectLookup` are scoped — no construction cycle as long as registration order is `Audit → ExecutorRegistry → Workspace → Identity` in `Program.cs`. Verify with a smoke test that starts the host.
- **`requiredRoleKey` batched validation**: `roleLookup.GetMissingAsync(IEnumerable<string>)` returns the unknown keys (or `[]`). Avoids one DB hit per contract.
- **`credentialsRef` leak**: only `ExecutorCredentialResolver.ResolveAsync` ever calls `Environment.GetEnvironmentVariable`. Greppable. Test in T-031 asserts no response body contains the literal value of a known seeded env var.
- **Replace-contracts race**: if a FEAT-004 lookup hits mid-replace, it can see an empty contract set briefly. Document as a known v1 limitation; FEAT-004 readers should treat a missing contract as a transient 404.
- **`ExecutorStatus` duplication**: keeping a Contracts-side enum copy of `ExecutorStatus` (Step 1) means callers across module boundaries don't need to reference the ExecutorRegistry module project. The module entity uses the Contracts enum directly.

## Acceptance Verification
- [ ] All ACs from the task definition pass when exercised manually via curl against a running host.
- [ ] `dotnet build` is green.
- [ ] DI smoke: `dotnet run --project src/DevHub.Api` starts without DI errors; `GET /api/admin/executors` returns 401 (auth required) before login, then `[]` after operator login.
- [ ] `ProjectService.Create` with an unbound `projectType` returns 409 (replaces the FEAT-002 TODO).
- [ ] Grep: `Environment.GetEnvironmentVariable(` appears only in `ExecutorCredentialResolver.cs`.
