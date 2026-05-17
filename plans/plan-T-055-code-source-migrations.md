# Implementation Plan: T-055 — EF migrations for Project.Repo / DefaultBranch + WorkItem.WorkBranch

## Task Reference
- **Task ID:** T-055 · **Type:** Database · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-1. Foundation step — all subsequent FEAT-008 tasks read from these columns.

## Overview
Three nullable text columns across two modules (Workspace, WorkItems), generated as two EF migrations, plus a changelog entry in `docs/data-model.md`. No service or DTO change in this task.

## Implementation Steps

### Step 1: Extend `Project` entity
**File:** `src/DevHub.Modules.Workspace/Entities/Project.cs` · Modify

Add two nullable properties after `Description`:

```csharp
public string? Repo { get; set; }
public string? DefaultBranch { get; set; }
```

Keep the file's `sealed` + `BaseEntity, ISoftDeletable` shape unchanged.

### Step 2: Extend `WorkItem` entity
**File:** `src/DevHub.Modules.WorkItems/Entities/WorkItem.cs` · Modify

Add one nullable property after `CreatedByMemberId`:

```csharp
public string? WorkBranch { get; set; }
```

### Step 3: Configure max-lengths in the Workspace DbContext
**File:** `src/DevHub.Modules.Workspace/Persistence/WorkspaceDbContext.cs` · Modify

Inside the `Project` configuration block (look for the existing `b.Property(p => p.Description).HasMaxLength(...)` pattern):

```csharp
b.Property(p => p.Repo).HasMaxLength(140);
b.Property(p => p.DefaultBranch).HasMaxLength(200);
```

Snake-case naming is handled by the existing naming convention — no `HasColumnName` needed.

### Step 4: Configure max-length in the WorkItems DbContext
**File:** `src/DevHub.Modules.WorkItems/Persistence/WorkItemsDbContext.cs` · Modify

```csharp
b.Property(w => w.WorkBranch).HasMaxLength(200);
```

### Step 5: Generate the Workspace migration
**Bash:**

```bash
dotnet ef migrations add AddProjectRepoAndDefaultBranch \
  --project src/DevHub.Modules.Workspace \
  --startup-project src/DevHub.Api
```

Inspect the generated migration — it should add two `text` columns to `project` with `IsNullable: true`. Reject and regenerate if it produced anything else (e.g. constraint changes from an unrelated drift).

### Step 6: Generate the WorkItems migration
**Bash:**

```bash
dotnet ef migrations add AddWorkItemWorkBranch \
  --project src/DevHub.Modules.WorkItems \
  --startup-project src/DevHub.Api
```

Same inspection — one `text` column on `work_item`.

### Step 7: Smoke the migration against a clean DB
**Bash:**

```bash
docker compose up -d
dotnet ef database update --project src/DevHub.Modules.Workspace --startup-project src/DevHub.Api
dotnet ef database update --project src/DevHub.Modules.WorkItems --startup-project src/DevHub.Api
psql -h localhost -p 5434 -U devhub devhub -c '\d project' -c '\d work_item'
```

Verify the three columns are present and nullable.

### Step 8: Update `docs/data-model.md`
**File:** `docs/data-model.md` · Modify

Project entity row table: add `repo (text, nullable, max 140)` and `default_branch (text, nullable, max 200)`. WorkItem entity row table: add `work_branch (text, nullable, max 200)`. Add a changelog entry at the bottom:

```
| 2026-05-17 | FEAT-008 | Project: added repo, default_branch (nullable). WorkItem: added work_branch (nullable). |
```

### Step 9: Run the test suite
**Bash:**

```bash
dotnet test
```

All existing tests continue to pass — the new columns default to `NULL` and existing fixtures are unaffected.

## Files Affected
| File | Action |
|------|--------|
| `src/DevHub.Modules.Workspace/Entities/Project.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Entities/WorkItem.cs` | Modify |
| `src/DevHub.Modules.Workspace/Persistence/WorkspaceDbContext.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Persistence/WorkItemsDbContext.cs` | Modify |
| `src/DevHub.Modules.Workspace/Migrations/<ts>_AddProjectRepoAndDefaultBranch.cs` | Create (generated) |
| `src/DevHub.Modules.WorkItems/Migrations/<ts>_AddWorkItemWorkBranch.cs` | Create (generated) |
| `docs/data-model.md` | Modify |

## Edge Cases & Risks
- **Test fixtures using positional `init` to construct Project / WorkItem.** The entities use property-init syntax (no positional record), so adding fields is backwards-compatible. Scan tests for `new Project { ... }` literals to confirm no fixture is broken.
- **Existing rows survive.** Both columns nullable, no default value, no backfill — straight ALTER TABLE ADD COLUMN. Postgres handles it instantly on small-to-medium tables.

## Acceptance Verification
- [ ] `Project` and `WorkItem` entity classes have the new properties with correct max-lengths.
- [ ] Two migration files exist and apply cleanly to a clean DB.
- [ ] `dotnet test` passes.
- [ ] `docs/data-model.md` reflects the new columns + changelog row.
