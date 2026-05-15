# Implementation Plan: T-009 — Stub initial migrations for the remaining four modules

## Task Reference
- **Task ID:** T-009
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** S
- **Rationale:** FEAT-001 AC-7 ("per-module `dotnet ef database update` works"). Proves the per-module migration pipeline before FEAT-003/004/005/006 add real entities.

## Overview
Generate empty initial migrations (schema-only) for ExecutorRegistry, WorkItems, Audit, and Notifications so every module owns its migration history table on the right schema.

## Implementation Steps

### Step 1: Set default schemas
**Files:** `src/DevHub.Modules.<Module>/<Module>DbContext.cs` (×4)
**Action:** Modify
In each DbContext's `OnModelCreating`:
- `ExecutorRegistry`: `modelBuilder.HasDefaultSchema("executor_registry");`
- `WorkItems`: `modelBuilder.HasDefaultSchema("work_items");`
- `Audit`: `modelBuilder.HasDefaultSchema("audit");`
- `Notifications`: `modelBuilder.HasDefaultSchema("notifications");`

### Step 2: Configure the migrations history table per schema
**Files:** `src/DevHub.Modules.<Module>/<Module>ModuleExtensions.cs` (×4)
**Action:** Modify
Inside `AddXModule`, extend the `UseNpgsql` call:
```csharp
opts.UseNpgsql(cfg.GetConnectionString("Postgres"),
    npg => npg.MigrationsHistoryTable("__ef_migrations_history", "executor_registry"))
```
…and analogously for the other three modules. This keeps each module's migration history isolated.

### Step 3: Generate the empty migrations
**Action:** Generate
```
dotnet ef migrations add Initial --project src/DevHub.Modules.ExecutorRegistry --startup-project src/DevHub.Api
dotnet ef migrations add Initial --project src/DevHub.Modules.WorkItems     --startup-project src/DevHub.Api
dotnet ef migrations add Initial --project src/DevHub.Modules.Audit         --startup-project src/DevHub.Api
dotnet ef migrations add Initial --project src/DevHub.Modules.Notifications --startup-project src/DevHub.Api
```
Each migration should contain only `migrationBuilder.EnsureSchema("...");` in `Up`.

### Step 4: Have each seeder migrate its module on startup
**Files:** `src/DevHub.Modules.<Module>/<Module>ModuleExtensions.cs` (×4)
**Action:** Modify
Add a tiny `MigrateOnStartup<TContext>` hosted service helper in DevHub.Contracts (one for all modules), and register it for each module. On `StartAsync`: `await db.Database.MigrateAsync(ct)`.

### Step 5: Apply locally
**Action:** Verify
With local Postgres up, run the API once. Inspect: `\dn` shows `workspace`, `identity`, `executor_registry`, `work_items`, `audit`, `notifications`. Each schema has its own `__ef_migrations_history` table with exactly one row.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `src/DevHub.Modules.<Module>/<Module>DbContext.cs` (×4) | Modify | `HasDefaultSchema(...)` |
| `src/DevHub.Modules.<Module>/<Module>ModuleExtensions.cs` (×4) | Modify | Per-schema migrations history + `MigrateOnStartup` hosted service |
| `src/DevHub.Modules.<Module>/Migrations/*` (×4) | Create | Empty `Initial` migration per module |
| `src/DevHub.Contracts/Persistence/MigrateOnStartup.cs` | Create | Reusable hosted-service helper |

## Edge Cases & Risks
- **Boot-time migration race** — when multiple instances start at once, they may race on `MigrateAsync`. EF Core's migrator advisory-locks the migrations history table, so concurrent calls are safe; document this behavior.
- **Schema collision with workspace/identity** — confirm the four new schemas don't shadow existing names. They don't.
- **Re-running an "empty" migration on a fresh DB** — `EnsureSchema` is idempotent, so re-applying is safe.

## Acceptance Verification
- [ ] `dotnet ef migrations list --project src/DevHub.Modules.<Module>` lists exactly one migration per module.
- [ ] After API boot against an empty DB, `\dn` shows all 6 schemas; each schema contains a `__ef_migrations_history` table with one row.
- [ ] Re-running the API leaves migration counts and row counts unchanged.
