# Implementation Plan: T-029 — Executor Registry foundation (entities + migration)

## Task Reference
- **Task ID:** T-029
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** S
- **Rationale:** FEAT-003 AC-1..AC-3 all depend on these tables existing. Mirrors the T-022 pattern (replace an empty stub migration with the real one).

## Overview
Land `ExecutorRegistration`, `ExecutorBinding`, `CheckpointContract` + `ExecutorStatus` enum under `DevHub.Modules.ExecutorRegistry`. Replace T-009's stub migration with a real `Initial`. No services yet — that's T-030.

## Implementation Steps

### Step 1: Enum
**File:** `src/DevHub.Modules.ExecutorRegistry/Enums/ExecutorStatus.cs` · **Action:** Create
```csharp
public enum ExecutorStatus { Active = 0, Paused = 1, Retired = 2 }
```

### Step 2: Entities
**Files:**
- `src/DevHub.Modules.ExecutorRegistry/Entities/ExecutorRegistration.cs` · Create
- `src/DevHub.Modules.ExecutorRegistry/Entities/ExecutorBinding.cs` · Create
- `src/DevHub.Modules.ExecutorRegistry/Entities/CheckpointContract.cs` · Create

`ExecutorRegistration : BaseEntity, ISoftDeletable` — `Key` (varchar 60), `DisplayName` (varchar 120), `BaseUrl` (varchar 500), `CredentialsRef` (varchar 120), `Status` (`ExecutorStatus`, default `Active`), `DeletedAt` (nullable timestamptz). Navigation: `ICollection<CheckpointContract> CheckpointContracts`.

`ExecutorBinding : BaseEntity, ISoftDeletable` — `ProjectType` (varchar 60), `ExecutorId` (FK), `DeletedAt`. **No nav to Project** — workspace boundary.

`CheckpointContract : BaseEntity` — `ExecutorId` (FK), `CheckpointKey` (varchar 60), `DisplayName` (varchar 120), `RequiredRoleKey` (varchar 60), `AllowedOutcomesJson` (string, stored as `jsonb`). **No `DeletedAt`** — replace semantics, not soft delete.

### Step 3: DbContext mappings
**File:** `src/DevHub.Modules.ExecutorRegistry/ExecutorRegistryDbContext.cs` · **Action:** Modify
```csharp
public DbSet<ExecutorRegistration> Executors => Set<ExecutorRegistration>();
public DbSet<ExecutorBinding> Bindings => Set<ExecutorBinding>();
public DbSet<CheckpointContract> CheckpointContracts => Set<CheckpointContract>();

protected override void OnModelCreating(ModelBuilder b)
{
    b.HasDefaultSchema(SchemaName);

    b.Entity<ExecutorRegistration>(e =>
    {
        e.Property(x => x.Key).HasMaxLength(60).IsRequired();
        e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        e.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
        e.Property(x => x.CredentialsRef).HasMaxLength(120).IsRequired();
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        e.HasIndex(x => x.Key).IsUnique().HasFilter("\"deleted_at\" IS NULL");
        e.HasMany(x => x.CheckpointContracts).WithOne().HasForeignKey(c => c.ExecutorId).OnDelete(DeleteBehavior.Cascade);
    });

    b.Entity<ExecutorBinding>(e =>
    {
        e.Property(x => x.ProjectType).HasMaxLength(60).IsRequired();
        e.HasIndex(x => x.ProjectType).IsUnique().HasFilter("\"deleted_at\" IS NULL");
        e.HasIndex(x => x.ExecutorId);
    });

    b.Entity<CheckpointContract>(e =>
    {
        e.Property(x => x.CheckpointKey).HasMaxLength(60).IsRequired();
        e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        e.Property(x => x.RequiredRoleKey).HasMaxLength(60).IsRequired();
        e.Property(x => x.AllowedOutcomesJson).HasColumnType("jsonb").IsRequired();
        e.HasIndex(x => new { x.ExecutorId, x.CheckpointKey }).IsUnique();
    });

    base.OnModelCreating(b);
}
```

### Step 4: Replace the stub migration
1. `dotnet ef migrations remove --project src/DevHub.Modules.ExecutorRegistry --startup-project src/DevHub.Api --context ExecutorRegistryDbContext`
2. `dotnet ef migrations add Initial --project src/DevHub.Modules.ExecutorRegistry --startup-project src/DevHub.Api --context ExecutorRegistryDbContext`
3. Verify the generated `Up()` calls `EnsureSchema("executor_registry")`, then creates the three tables + the unique partial indexes + the (executor_id, checkpoint_key) unique index.

### Step 5: Migrate-on-startup wiring
**File:** `src/DevHub.Modules.ExecutorRegistry/ExecutorRegistryModuleExtensions.cs` · **Action:** Modify
Confirm `MigrateOnStartup<ExecutorRegistryDbContext>()` is registered (was added in T-002).

## Files Affected
| File | Action |
|------|--------|
| `Entities/ExecutorRegistration.cs`, `ExecutorBinding.cs`, `CheckpointContract.cs` | Create |
| `Enums/ExecutorStatus.cs` | Create |
| `ExecutorRegistryDbContext.cs` | Modify |
| `Migrations/*` | Replace |
| `ExecutorRegistryModuleExtensions.cs` | Modify (verify only) |

## Edge Cases & Risks
- **Existing test DBs** carry the empty stub migration. Testcontainers spins up fresh DBs per assembly — no risk. Dev DBs reapply migrations on startup; the replacement is a no-op on an empty DB and adds the new tables on populated dev DBs.
- **`AllowedOutcomesJson` cap:** no `MaxLength` on `jsonb`; reasonable since the list is short (e.g. `["approve","reject","revise"]`). Document in the entity that v1 contracts ship at most ~10 outcomes.
- **`CheckpointContract` lacks `deleted_at`:** intentional — replace semantics. Document on the entity class.

## Acceptance Verification
- [ ] `dotnet ef migrations list --project src/DevHub.Modules.ExecutorRegistry` shows one migration.
- [ ] `dotnet ef database update --project src/DevHub.Modules.ExecutorRegistry --startup-project src/DevHub.Api` applies cleanly on an empty DB.
- [ ] `psql` inspection: `executor_registry.executor_registrations`, `executor_bindings`, `checkpoint_contracts` exist with the indexes from Step 3.
- [ ] `dotnet build` is green; no new tests required at this step.
