# Implementation Plan: T-042 — Notifications foundation (entity + migration)

## Task Reference
- **Task ID:** T-042 · **Type:** Backend · **Workflow:** standard · **Complexity:** S
- **Rationale:** Every other FEAT-005 task reads or writes `PendingActionSignal`.

## Overview
Replace T-009's stub Notifications migration with a real one. One entity, `PendingActionSignal`. Mirrors the T-022/T-029/T-034 replacement pattern.

## Implementation Steps

### Step 1: Entity
**File:** `src/DevHub.Modules.Notifications/Entities/PendingActionSignal.cs` · Create

```csharp
public sealed class PendingActionSignal : BaseEntity
{
    public required Guid MemberId { get; set; }
    public required Guid ProjectId { get; set; }
    public required Guid WorkItemId { get; set; }
    public required string CheckpointKey { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
}
```

No nav properties to other modules — FK columns only.

### Step 2: DbContext mappings
**File:** `src/DevHub.Modules.Notifications/NotificationsDbContext.cs` · Modify

```csharp
public DbSet<PendingActionSignal> PendingActionSignals => Set<PendingActionSignal>();

protected override void OnModelCreating(ModelBuilder b)
{
    b.HasDefaultSchema(SchemaName);
    b.Entity<PendingActionSignal>(e =>
    {
        e.Property(x => x.CheckpointKey).HasMaxLength(60).IsRequired();
        e.HasIndex(x => new { x.MemberId, x.WorkItemId, x.CheckpointKey })
            .IsUnique()
            .HasFilter("\"dismissed_at\" IS NULL");
        e.HasIndex(x => new { x.MemberId, x.ProjectId })
            .HasFilter("\"dismissed_at\" IS NULL");
    });
    base.OnModelCreating(b);
}
```

### Step 3: Replace the stub migration
1. `dotnet ef migrations remove --project src/DevHub.Modules.Notifications --startup-project src/DevHub.Api --context NotificationsDbContext --force`
2. `dotnet ef migrations add Initial --project src/DevHub.Modules.Notifications --startup-project src/DevHub.Api --context NotificationsDbContext`
3. Verify `Up()` creates the table + the two partial indexes.

### Step 4: Migrate-on-startup wiring
**File:** `src/DevHub.Modules.Notifications/NotificationsModuleExtensions.cs` · Verify
`MigrateOnStartup<NotificationsDbContext>` was registered at T-002. No service additions yet — T-043 lands those.

## Files Affected
| File | Action |
|------|--------|
| `Entities/PendingActionSignal.cs` | Create |
| `NotificationsDbContext.cs` | Modify |
| `Migrations/*` | Replace |

## Edge Cases & Risks
- **`DismissedAt` is nullable timestamptz, not the soft-delete shape.** Rows are kept briefly for UI fade then can be hard-purged by a FEAT-006 archive task. Document in the entity comment.
- **Partial-unique index** generates a Postgres `WHERE "dismissed_at" IS NULL` filter — EF Core 10's `HasFilter(...)` handles this correctly.
- **Cross-module FKs by id only.** Workspace's `member_id` / `project_id` and WorkItems' `work_item_id` are *not* foreign-keyed across modules. The reconciler in T-043 handles consistency.

## Acceptance Verification
- [ ] `dotnet ef migrations list --project src/DevHub.Modules.Notifications` shows one migration.
- [ ] `dotnet ef database update --project src/DevHub.Modules.Notifications --startup-project src/DevHub.Api` applies cleanly.
- [ ] Full backend suite (94 tests) still green.
