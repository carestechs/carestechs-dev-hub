# Implementation Plan: T-005 — Workspace module: Member + Role entities + initial migration + seed

## Task Reference
- **Task ID:** T-005
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** M
- **Rationale:** The seed operator is required for FEAT-001 AC-3 (first login). `Member` is referenced by Identity's `IdentityCredential.member_id` in T-006.

## Overview
Land the Workspace module's entities (Member, Role, plus minimal scaffolds for Team / Project / ProjectMembership / RoleAssignment), produce the initial migration under the `workspace` schema, and run an idempotent startup seeder that ensures the `operator` system role and the seeded operator member exist.

## Implementation Steps

### Step 1: Entity scaffolds
**Files:** `src/DevHub.Modules.Workspace/Entities/*.cs`
**Action:** Create
- `Member.cs` — `BaseEntity`, `ISoftDeletable`. Fields: `DisplayName (string, 120)`, `Email (string, 255)`, `Status (MemberStatus)`.
- `Role.cs` — `BaseEntity` (no soft delete). Fields: `Key (string, 60)`, `Name (string, 120)`, `Description (string?)`, `IsSystem (bool)`.
- `Team.cs`, `Project.cs`, `ProjectMembership.cs`, `RoleAssignment.cs` — minimal scaffolds with the FK columns that Member/Role need to coexist (`Team.Name`, `Project.Name/Slug/ProjectType/OwningTeamId`, etc. — full surface lands in FEAT-002).
- `Enums/MemberStatus.cs` — `Active`, `Suspended`, `Invited`.

Per `docs/data-model.md`, all six are `BaseEntity` + `ISoftDeletable` except `Role`.

### Step 2: Configure mappings
**File:** `src/DevHub.Modules.Workspace/WorkspaceDbContext.cs`
**Action:** Modify
Add `DbSet<Member>`, `DbSet<Role>`, and `DbSet<...>` for the others. In `OnModelCreating`:
- Apply `HasDefaultSchema("workspace")` (already set in T-002).
- `modelBuilder.Entity<Member>().HasIndex(m => m.Email).IsUnique().HasFilter("deleted_at IS NULL");`
- `modelBuilder.Entity<Role>().HasIndex(r => r.Key).IsUnique();`
- Global query filter for `ISoftDeletable`: `modelBuilder.Entity<Member>().HasQueryFilter(m => m.DeletedAt == null);` (apply to every soft-deletable entity).
- Enum-as-string conversion for `Status`.

### Step 3: Initial migration
**Action:** Generate
```
dotnet ef migrations add Initial \
  --project src/DevHub.Modules.Workspace \
  --startup-project src/DevHub.Api
```
Inspect the generated migration: tables must be `workspace.members`, `workspace.roles`, etc., columns must be snake_case, `created_at`/`updated_at`/`deleted_at` are `timestamptz`. The `migrations_history` table must live under the `workspace` schema (via `migrationsHistoryTable: ..., schema: "workspace"`).

### Step 4: Seeder
**File:** `src/DevHub.Modules.Workspace/Seeding/WorkspaceSeeder.cs`
**Action:** Create
`IHostedService`. On `StartAsync`:
1. Resolve `WorkspaceDbContext` from a created scope.
2. Run `await db.Database.MigrateAsync(cancellationToken)`.
3. Upsert role: if `roles` has no row with `key='operator'`, insert one with `IsSystem = true`.
4. Read `OperatorSeed:Email` / `OperatorSeed:DisplayName` from config. Upsert member with that email; do NOT touch credentials (T-006 handles that).
5. SaveChanges.

Idempotency is critical — every step must check existence first.

### Step 5: Register the seeder
**File:** `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs`
**Action:** Modify
Add `services.AddHostedService<WorkspaceSeeder>();` inside `AddWorkspaceModule`.

### Step 6: Verify locally
**Action:** Verify
With Postgres up (T-003), run the API. Confirm the migration table exists, `workspace.roles` has the operator row, `workspace.members` has the seeded member. Restart — counts stay the same.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `src/DevHub.Modules.Workspace/Entities/Member.cs` | Create | Member entity |
| `src/DevHub.Modules.Workspace/Entities/Role.cs` | Create | Role entity |
| `src/DevHub.Modules.Workspace/Entities/Team.cs` (and others) | Create | Minimal scaffolds for FK targets |
| `src/DevHub.Modules.Workspace/Entities/Enums/MemberStatus.cs` | Create | Enum |
| `src/DevHub.Modules.Workspace/WorkspaceDbContext.cs` | Modify | DbSets, indexes, query filters |
| `src/DevHub.Modules.Workspace/Migrations/*` | Create | Initial migration |
| `src/DevHub.Modules.Workspace/Seeding/WorkspaceSeeder.cs` | Create | Idempotent role + member seed |
| `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs` | Modify | Register seeder |

## Edge Cases & Risks
- **Two app instances seeding concurrently** — wrap upserts in a transaction with `SERIALIZABLE` isolation, or do `INSERT ... ON CONFLICT DO NOTHING` via `ExecuteSqlInterpolatedAsync`. Pick the latter for simplicity.
- **Email uniqueness with soft delete** — the unique index has a `WHERE deleted_at IS NULL` filter, so two soft-deleted rows with the same email are allowed but only one live row is.
- **Migration ordering across modules** — each module's migration is independent; ordering is *not* guaranteed. Confirm no cross-module FK in this task (there is none — Member is referenced only by ID from other modules).

## Acceptance Verification
- [ ] `dotnet ef database update --project src/DevHub.Modules.Workspace --startup-project src/DevHub.Api` applies cleanly on an empty database.
- [ ] After startup, `SELECT count(*) FROM workspace.roles WHERE key='operator' AND is_system=true` returns 1.
- [ ] After startup, `SELECT count(*) FROM workspace.members WHERE email=$OPERATOR_SEED_EMAIL` returns 1.
- [ ] Re-running the API produces no duplicates and no exceptions.
