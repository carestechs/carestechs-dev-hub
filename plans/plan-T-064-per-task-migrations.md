# Implementation Plan: T-064 — EF migrations for per-task fields

## Task Reference
- **Task ID:** T-064 · **Type:** Database · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-1, AC-3. Foundation — every other FEAT-009 task reads from these columns.

## Overview
Three new columns across three modules (ExecutorRegistry, WorkItems, Notifications) and one uniqueness rewrite on `pending_action_signal`. The COALESCE-with-filter index is raw-SQL in the migration body.

## Implementation Steps

### Step 1: Extend `CheckpointContract`
**File:** `src/DevHub.Modules.ExecutorRegistry/Entities/CheckpointContract.cs` · Modify

```csharp
public bool PerTask { get; set; }
```

Default `false` via C# property initialization. No `required` keyword (existing rows backfill to `false`).

### Step 2: Extend `WorkItem`
**File:** `src/DevHub.Modules.WorkItems/Entities/WorkItem.cs` · Modify

```csharp
public string? CurrentTaskId { get; set; }
```

### Step 3: Extend `PendingActionSignal`
**File:** `src/DevHub.Modules.Notifications/Entities/PendingActionSignal.cs` · Modify

```csharp
public string? TaskId { get; set; }
```

### Step 4: DbContext property configs
**Files:**
- `src/DevHub.Modules.ExecutorRegistry/ExecutorRegistryDbContext.cs` — add `b.Property(c => c.PerTask).HasDefaultValue(false);`
- `src/DevHub.Modules.WorkItems/WorkItemsDbContext.cs` — add `e.Property(x => x.CurrentTaskId).HasMaxLength(60);`
- `src/DevHub.Modules.Notifications/NotificationsDbContext.cs` — add `e.Property(x => x.TaskId).HasMaxLength(60);`

For the Notifications index, keep the existing C# `HasIndex(...)` non-unique (or drop it — see Step 6). The unique constraint lives in raw SQL.

### Step 5: Generate the migrations
**Bash:**

```bash
dotnet ef migrations add AddCheckpointContractPerTask \
  --project src/DevHub.Modules.ExecutorRegistry \
  --startup-project src/DevHub.Api \
  --context ExecutorRegistryDbContext

dotnet ef migrations add AddWorkItemCurrentTaskId \
  --project src/DevHub.Modules.WorkItems \
  --startup-project src/DevHub.Api \
  --context WorkItemsDbContext

dotnet ef migrations add AddPendingActionTaskId \
  --project src/DevHub.Modules.Notifications \
  --startup-project src/DevHub.Api \
  --context NotificationsDbContext
```

Inspect each generated migration — only `AddColumn` (and possibly `AlterColumn` for default-value) ops. Reject and regenerate if anything else surfaces.

### Step 6: Rewrite the Notifications uniqueness in raw SQL
**File:** the generated `*_AddPendingActionTaskId.cs` migration · Modify

After the `AddColumn` op, drop the old per-key index if it was unique, then add the partial COALESCE index:

```csharp
migrationBuilder.Sql(@"
    DROP INDEX IF EXISTS ""notifications"".""ix_pending_action_signal_member_id_work_item_id_checkpoint_key"";
    CREATE UNIQUE INDEX ""ux_pending_action_signal_active_per_task""
        ON ""notifications"".""pending_action_signal"" (
            ""member_id"",
            ""work_item_id"",
            ""checkpoint_key"",
            COALESCE(""task_id"", '<root>')
        )
        WHERE ""dismissed_at"" IS NULL;
");
```

(Schema name + exact index identifier need to be verified against the snapshot — adjust to match.)

In the `Down` method, mirror with `DROP INDEX` and restore the old non-COALESCE index if needed for clean rollback.

### Step 7: `docs/data-model.md`
**File:** `docs/data-model.md` · Modify

Three entity tables (CheckpointContract, WorkItem, PendingActionSignal) get new field rows. Changelog entry:

```
| 2026-05-17 (FEAT-009 / T-064) | CheckpointContract gained per_task (bool, default false). WorkItem gained current_task_id (nullable text). PendingActionSignal gained task_id (nullable text); active-row uniqueness rewritten to (member_id, work_item_id, checkpoint_key, COALESCE(task_id, '<root>')) where dismissed_at IS NULL. |
```

### Step 8: Run the suite
**Bash:**

```bash
dotnet test
```

182/182 still green. Existing fixtures unaffected — all new columns are nullable / defaulted.

## Files Affected
| File | Action |
|------|--------|
| `src/DevHub.Modules.ExecutorRegistry/Entities/CheckpointContract.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Entities/WorkItem.cs` | Modify |
| `src/DevHub.Modules.Notifications/Entities/PendingActionSignal.cs` | Modify |
| All three DbContexts | Modify |
| 3 new migration files | Create |
| `docs/data-model.md` | Modify |

## Edge Cases & Risks
- **COALESCE-with-filter** isn't expressible in `modelBuilder.HasIndex(...)`. Raw SQL in the migration is the path — see Step 6. The downside: EF's snapshot might not perfectly track this index. Acceptable; future migrations will see "no change" on the index, which is the desired no-op.
- **Rollback**: dropping the unique index in `Down` is necessary, but the original non-task-aware unique index may not be easily restorable if Postgres has already enforced a violation. In practice rollback won't happen in production; document the constraint in a comment on the `Up` method.
- **Existing pending-action rows survive.** Active rows get `task_id = NULL`, the COALESCE folds to `'<root>'`, uniqueness behavior matches today.

## Acceptance Verification
- [ ] Three new columns present with the right types + nullability/defaults.
- [ ] Three migrations apply cleanly.
- [ ] `dotnet test` 182/182 green.
- [ ] `docs/data-model.md` reflects the new shape + changelog row.
