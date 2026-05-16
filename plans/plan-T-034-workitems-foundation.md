# Implementation Plan: T-034 — WorkItems foundation (entities + migration)

## Task Reference
- **Task ID:** T-034 · **Type:** Backend · **Workflow:** standard · **Complexity:** S
- **Rationale:** Every later FEAT-004 task reads or writes one of these tables.

## Overview
Replace T-009's empty WorkItems migration with the real one. Two entities (`WorkItem`, `CheckpointSignal`) under `work_items` schema. Mirrors the T-022 (audit) and T-029 (registry) replacement patterns.

## Implementation Steps

### Step 1: Entities
**Files (Create):**
- `src/DevHub.Modules.WorkItems/Entities/WorkItem.cs`
- `src/DevHub.Modules.WorkItems/Entities/CheckpointSignal.cs`

`WorkItem : BaseEntity` (no `ISoftDeletable` — cancellation is a status):
- `ProjectId`, `ExecutorId`, `ExecutorCorrelationMarker` (varchar 120), `Title` (varchar 255), `CurrentStatus` (varchar 60), `CurrentCheckpointKey` (varchar 60 nullable), `CreatedByMemberId`.

`CheckpointSignal : BaseEntity`:
- `WorkItemId` (FK), `CheckpointKey` (varchar 60), `Outcome` (varchar 60), `PayloadJson` (string, jsonb, nullable), `SignaledByMemberId`, `SignaledAt`, `ExecutorResponseStatus` (int nullable), `ExecutorResponseAt` (timestamptz nullable), `IdempotencyKey` (varchar 60 nullable).

### Step 2: DbContext mappings
**File:** `src/DevHub.Modules.WorkItems/WorkItemsDbContext.cs` · Modify

```csharp
public DbSet<WorkItem> WorkItems => Set<WorkItem>();
public DbSet<CheckpointSignal> CheckpointSignals => Set<CheckpointSignal>();

protected override void OnModelCreating(ModelBuilder b)
{
    b.HasDefaultSchema(SchemaName);

    b.Entity<WorkItem>(e =>
    {
        e.Property(x => x.ExecutorCorrelationMarker).HasMaxLength(120).IsRequired();
        e.Property(x => x.Title).HasMaxLength(255).IsRequired();
        e.Property(x => x.CurrentStatus).HasMaxLength(60).IsRequired();
        e.Property(x => x.CurrentCheckpointKey).HasMaxLength(60);
        e.HasIndex(x => new { x.ExecutorId, x.ExecutorCorrelationMarker }).IsUnique();
        e.HasIndex(x => new { x.ProjectId, x.CurrentStatus });
    });

    b.Entity<CheckpointSignal>(e =>
    {
        e.Property(x => x.CheckpointKey).HasMaxLength(60).IsRequired();
        e.Property(x => x.Outcome).HasMaxLength(60).IsRequired();
        e.Property(x => x.PayloadJson).HasColumnType("jsonb");
        e.Property(x => x.IdempotencyKey).HasMaxLength(60);
        e.HasIndex(x => new { x.WorkItemId, x.SignaledAt }).IsDescending(false, true);
        e.HasIndex(x => new { x.WorkItemId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"idempotency_key\" IS NOT NULL");
    });
    base.OnModelCreating(b);
}
```

### Step 3: Replace the stub migration
1. `dotnet ef migrations remove --project src/DevHub.Modules.WorkItems --startup-project src/DevHub.Api --context WorkItemsDbContext --force`
2. `dotnet ef migrations add Initial --project src/DevHub.Modules.WorkItems --startup-project src/DevHub.Api --context WorkItemsDbContext`
3. Confirm the generated `Up()` creates both tables + the three indexes + the partial-unique idempotency index.

### Step 4: Module DI
**File:** `src/DevHub.Modules.WorkItems/WorkItemsModuleExtensions.cs` · Verify
`MigrateOnStartup<WorkItemsDbContext>` is already registered (from T-002). No service registrations yet — those land in T-036.

## Files Affected
| File | Action |
|------|--------|
| `Entities/WorkItem.cs`, `CheckpointSignal.cs` | Create |
| `WorkItemsDbContext.cs` | Modify |
| `Migrations/*` | Replace |

## Edge Cases & Risks
- **Empty workspaces** — no `Project` rows means the FK reference (`project_id`) has no FK enforced (cross-module by ID per CLAUDE.md). Acceptable; FEAT-006's operator dashboard will surface orphans.
- **`IdempotencyKey` partial-unique** — Postgres-only `HasFilter`. EF Core 10 generates `WHERE "idempotency_key" IS NOT NULL` correctly. Verify the migration includes the filter.
- **`SignaledAt` default** — set in code (`DateTimeOffset.UtcNow` in service) rather than DB default, so it always carries the signal-time-of-day in our control.

## Acceptance Verification
- [ ] `dotnet ef migrations list --project src/DevHub.Modules.WorkItems` shows one migration.
- [ ] `dotnet ef database update --project src/DevHub.Modules.WorkItems --startup-project src/DevHub.Api` applies cleanly.
- [ ] Full test suite stays green (71/71 backend + 114/114 SPA after T-033).
