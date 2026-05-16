# Implementation Plan: T-047 — Audit query service + AC-1/AC-2/AC-5 invariant tests

## Task Reference
- **Task ID:** T-047 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** The write side already exists (T-022's `IAuditWriter`, used by every mutation in every FEAT). What's missing is the read-side surface and codified invariants that catch regressions.

## Overview
Three deliverables in one PR:
1. **`IAuditQueryService`** + read DTOs in the Audit module.
2. **Static `AppendOnlyAuditInvariantTests`** — regex scan over `src/` that fails if anyone adds an UPDATE/DELETE against `audit_entries` (AC-5).
3. **`AuditMutationSweepTests` + `AuditDenySweepTests`** — integration tests that exercise every known mutation and deny surface and assert audit rows materialize (AC-1, AC-2).

## Implementation Steps

### Step 1: DTOs + filter
**Files (Create):**
- `src/DevHub.Modules.Audit/DTOs/AuditEntryDto.cs`
- `src/DevHub.Modules.Audit/DTOs/AuditFilter.cs`

```csharp
public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    AuditActorDto? ActingMember,
    Guid? ProjectId,
    string TargetType,
    Guid? TargetId,
    string Action,
    AuditOutcome Outcome,
    string? Reason,
    JsonElement? Details);

public sealed record AuditActorDto(Guid Id, string DisplayName);

public sealed class AuditFilter
{
    public Guid? ActingMemberId { get; init; }
    public string? TargetType { get; init; }
    public string? Action { get; init; }
    public AuditOutcome? Outcome { get; init; }
    public Guid? ProjectId { get; init; } // ignored by the project-scoped query
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}
```

### Step 2: Query service
**File:** `src/DevHub.Modules.Audit/Services/IAuditQueryService.cs` · Create

```csharp
public interface IAuditQueryService
{
    Task<PagedEnvelopeDto<AuditEntryDto>> ListForProjectAsync(
        Guid projectId, AuditFilter filter, PageRequest page, CancellationToken ct = default);

    Task<PagedEnvelopeDto<AuditEntryDto>> ListAsync(
        AuditFilter filter, PageRequest page, CancellationToken ct = default);
}
```

**File:** `src/DevHub.Modules.Audit/Services/AuditQueryService.cs` · Create

`AuditQueryService(AuditDbContext db, IMemberLookup members)`:
- Builds a base `AsNoTracking()` query from `db.AuditEntries`.
- Applies filter predicates conditionally.
- Defaults `sortBy=occurredAt, sortDir=desc`.
- Resolves `acting_member_id` → `displayName` via `members.FindByIdAsync` per row (v1 N+1 OK).
- Parses `details_json` to `JsonElement?`.

For project-scoped: prepends `Where(a => a.ProjectId == projectId)` before filter application. For admin: respects `filter.ProjectId` if present.

### Step 3: DI
**File:** `src/DevHub.Modules.Audit/AuditModuleExtensions.cs` · Modify
```csharp
services.AddScoped<IAuditQueryService, AuditQueryService>();
```

### Step 4: Invariant test (AC-5)
**File:** `tests/DevHub.Modules.Audit.Tests/AppendOnlyAuditInvariantTests.cs` · Create

```csharp
public class AppendOnlyAuditInvariantTests
{
    [Fact]
    public void No_source_file_mutates_audit_entries()
    {
        var srcRoot = LocateSrcRoot();
        var violations = new List<string>();
        var forbidden = new[]
        {
            new Regex(@"AuditEntries\s*\.\s*(Update|Remove|RemoveRange)\b", RegexOptions.Compiled),
            new Regex(@"audit_entries.*ExecuteUpdate|audit_entries.*ExecuteDelete", RegexOptions.Compiled),
        };

        foreach (var file in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            // Skip generated migrations — Initial only INSERTs; future migrations should be
            // reviewed explicitly if they touch audit rows.
            if (file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")) continue;

            var text = File.ReadAllText(file);
            foreach (var re in forbidden)
            {
                if (re.IsMatch(text)) violations.Add($"{file}: {re}");
            }
        }

        violations.Should().BeEmpty(
            "audit_entries is append-only — only AuditWriter.Add(...) is permitted");
    }

    private static string LocateSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        return Path.Combine(dir!.FullName, "src");
    }
}
```

### Step 5: Mutation sweep (AC-1)
**File:** `tests/DevHub.Modules.Audit.Tests/AuditMutationSweepTests.cs` · Create

`[Collection("postgres")]`, factory `UseFakeExecutor = true`. Seeds operator + approve contract. Each test exercises one mutation surface end-to-end via HTTP and asserts `AuditEntries` contains the matching Granted row:

- `team:create` → POST `/api/teams`
- `member:invite` → POST `/api/members`
- `executor:create` → POST `/api/admin/executors`
- `executor-binding:create` → POST `/api/admin/executor-bindings`
- `project:create` → POST `/api/projects`
- `project:membership:add` → POST `/api/projects/{}/memberships`
- `workitem:start` → POST `/api/projects/{}/work-items`
- `checkpoint:signal` → POST `.../checkpoints/{key}/signal`
- `workitem:cancel` → POST `.../cancel`

One `[Fact]` per surface. Assertions: at least one `AuditEntry` exists with `Action == "<expected>"` and `Outcome == Granted`.

### Step 6: Deny sweep (AC-2)
**File:** `tests/DevHub.Modules.Audit.Tests/AuditDenySweepTests.cs` · Create

Same shape, deny path:
- Fresh non-operator member calls each operator-only endpoint → expect 403 + Denied audit row with non-empty `Reason`.
- Non-member tries to read a project → 403 + Denied row.
- Non-member tries to signal a work item → 403 + Denied row.

### Step 7: Test project deps
**File:** `tests/DevHub.Modules.Audit.Tests/DevHub.Modules.Audit.Tests.csproj` · Modify
Add refs to `DevHub.Modules.Workspace`, `Identity`, `WorkItems`, `Notifications`, `ExecutorRegistry` so the sweep tests can use existing helpers.

## Files Affected
| File | Action |
|------|--------|
| `Audit/DTOs/AuditEntryDto.cs`, `AuditFilter.cs` | Create |
| `Audit/Services/IAuditQueryService.cs`, `AuditQueryService.cs` | Create |
| `Audit/AuditModuleExtensions.cs` | Modify (DI) |
| `Audit.Tests/AppendOnlyAuditInvariantTests.cs` | Create |
| `Audit.Tests/AuditMutationSweepTests.cs`, `AuditDenySweepTests.cs` | Create |
| `Audit.Tests/DevHub.Modules.Audit.Tests.csproj` | Modify (refs) |

## Edge Cases & Risks
- **Static grep false positives.** Variable names like `auditEntriesUpdated` (with underscore not dot) won't match. The regex uses `\.\s*` so only method calls trip it. Documented.
- **Test reads audit rows.** `IAuditQueryService` joins to `IMemberLookup`. If the seeded operator has been deleted, the row's `actingMember` is `null` — acceptable.
- **Migration files skipped.** A future migration that adds new columns to audit_entries is fine; one that drops rows would need explicit reviewer approval (the regex only catches code-level mutations).

## Acceptance Verification
- [ ] `dotnet build` clean.
- [ ] Each sweep test passes against the existing implementation (proves AC-1/AC-2 are already met by FEAT-001..005).
- [ ] Invariant test passes against `src/`.
- [ ] Existing 106 tests still pass.
