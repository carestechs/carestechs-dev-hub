# Data Model

## Overview

DevHub domain models the **human and organizational layer** above one or more headless lifecycle executors. Every entity is project-scoped or one level above (workspace primitives). Lifecycle state itself lives in the executors — DevHub holds only an **index** of work items (id, project, type, latest known status snapshot, executor correlation marker), never the full execution graph.

Six modules own data; each owns its own `DbContext` and its own schema. Cross-module references are by **ID + `DevHub.Contracts/` interface only** — no navigation properties cross module boundaries.

### Key Modeling Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Primary key strategy | UUIDs generated in C# | No sequential leaks; safe for URLs; end-to-end identity from API to DB |
| Soft vs hard deletes | Soft deletes (`deleted_at` nullable timestamptz) on **workspace primitives** (Project, Team, Member, ProjectMembership, ExecutorRegistration, ExecutorBinding); hard on transient queues; **append-only** for AuditEntry | Org structure needs undo + history; audit log must never lose evidence |
| Timestamp handling | `timestamptz`, C# `DateTimeOffset`, always UTC at rest | Timezone consistency across distributed members |
| Naming | snake_case for tables and columns (EF Core naming convention) | PostgreSQL idiom; no quoting required |
| Cross-module reference | ID only, resolved via `DevHub.Contracts` interfaces | Module isolation; lets a module be extracted later |
| Lifecycle state ownership | Executor owns full state; DevHub caches `latest_state_snapshot` for the work-item index only | Stakeholder rule 4: "transparent facade for live state" — don't accumulate state |
| Authorization scope | Every authorized resource resolves to `(member, role, project, target)` | Stakeholder Product Philosophy #5 |

## Module Ownership

| Module | Entities Owned | DbContext |
|--------|---------------|-----------|
| Workspace | Project, Team, Member, Role, RoleAssignment, ProjectMembership | `WorkspaceDbContext` |
| Identity | IdentityCredential, RefreshToken | `IdentityDbContext` |
| ExecutorRegistry | ExecutorRegistration, ExecutorBinding, CheckpointContract | `ExecutorRegistryDbContext` |
| WorkItems | WorkItem, CheckpointSignal | `WorkItemsDbContext` |
| Audit | AuditEntry | `AuditDbContext` |
| Notifications | PendingActionSignal | `NotificationsDbContext` |

## Entity Definitions

### Project

> *Module: Workspace — A unit of work owned by a team. Every work item, run, approval, and audit entry is scoped to exactly one project.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | Primary key |
| name | varchar(120) | Required, unique per workspace (when not deleted) | Human-readable project name |
| slug | varchar(60) | Required, unique per workspace (when not deleted), `^[a-z0-9-]+$` | URL-safe identifier |
| project_type | varchar(60) | Required | Routes the project to a lifecycle executor via `ExecutorBinding` |
| owning_team_id | UUID | Required, FK → Team.id | The team that owns this project |
| description | text | Optional | Free-form description |
| repo | varchar(140) | Optional | GitHub `owner/name` (no scheme, no `.git`). Forwarded as `intake.codeSource.repo` to the executor on work-item start. Validated at the DevHub boundary per the orchestrator's `intake.codeSource` schema. |
| default_branch | varchar(200) | Optional | Default git branch name (e.g. `main`). Forwarded as `intake.codeSource.baseBranch` on start. Same validation rules as a git branch shorthand (no whitespace, no leading `/`, no `..`, no control chars). |
| created_at | timestamptz | Required, Auto | Record creation timestamp |
| updated_at | timestamptz | Required, Auto | Last modification timestamp |
| deleted_at | timestamptz | Nullable (soft delete) | Soft-delete marker |

**Indexes:**
- Unique on `(slug)` where `deleted_at IS NULL`.
- Unique on `(name)` where `deleted_at IS NULL`.
- Index on `(owning_team_id)`.
- Index on `(project_type)`.

**Business Rules:**
- A project has exactly one owning team. Members from other teams may still hold a `ProjectMembership` (i.e. participate cross-team) but the *owning* team is singular.
- `project_type` MUST resolve to an existing `ExecutorBinding` at creation time. Otherwise the project cannot start work.

---

### Team

> *Module: Workspace — A named group of members. A team owns one or more projects.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | Primary key |
| name | varchar(120) | Required, unique among non-deleted teams | Human-readable name |
| description | text | Optional | Free-form description |
| created_at | timestamptz | Required, Auto | |
| updated_at | timestamptz | Required, Auto | |
| deleted_at | timestamptz | Nullable | Soft-delete marker |

**Business Rules:**
- Deleting a team is allowed only if it owns no non-deleted projects.

---

### Member

> *Module: Workspace — A human identity inside this workspace. The single subject of every authorization check.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | Primary key |
| display_name | varchar(120) | Required | Shown in UI |
| email | varchar(255) | Required, unique among non-deleted members | Login + contact |
| status | enum MemberStatus | Required, default `Active` | `Active`, `Suspended`, `Invited` |
| created_at | timestamptz | Required, Auto | |
| updated_at | timestamptz | Required, Auto | |
| deleted_at | timestamptz | Nullable | Soft-delete marker |

**Business Rules:**
- A `Suspended` or soft-deleted member fails every authorization check, regardless of memberships.

---

### Role

> *Module: Workspace — A named capability set evaluated against a project. Roles are workspace-global definitions; assignments are per-project.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | Primary key |
| key | varchar(60) | Required, unique, `^[a-z][a-z0-9_-]*$` | Stable identifier (e.g. `reviewer`, `approver`, `operator`) |
| name | varchar(120) | Required | Human-readable name |
| description | text | Optional | What this role can do |
| is_system | boolean | Required, default false | True for DevHub-shipped roles (e.g. `operator`); cannot be deleted |
| created_at | timestamptz | Required, Auto | |
| updated_at | timestamptz | Required, Auto | |

**Business Rules:**
- The `operator` role is a system role with admin scope across executors (registry management, dashboard).

---

### ProjectMembership

> *Module: Workspace — Pairs a Member with a Project. The presence of a row means "this member belongs to this project"; the assigned roles decide what they may do.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | Primary key |
| project_id | UUID | Required, FK → Project.id | |
| member_id | UUID | Required, FK → Member.id | |
| created_at | timestamptz | Required, Auto | When the membership was granted |
| created_by_member_id | UUID | Required, FK → Member.id | Who granted it |
| deleted_at | timestamptz | Nullable | Soft-delete marker |

**Indexes:**
- Unique on `(project_id, member_id)` where `deleted_at IS NULL`.

**Business Rules:**
- A member without an active `ProjectMembership` on a project fails every authorization check against that project, regardless of role assignments.

---

### RoleAssignment

> *Module: Workspace — Grants a Role to a ProjectMembership.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | |
| project_membership_id | UUID | Required, FK → ProjectMembership.id | |
| role_id | UUID | Required, FK → Role.id | |
| created_at | timestamptz | Required, Auto | |
| created_by_member_id | UUID | Required, FK → Member.id | |
| deleted_at | timestamptz | Nullable | |

**Indexes:**
- Unique on `(project_membership_id, role_id)` where `deleted_at IS NULL`.

---

### IdentityCredential

> *Module: Identity — Authentication material for a Member. Email/password in v1; the schema is intended to accept federated providers later.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | |
| member_id | UUID | Required, unique | Cross-module reference to `Workspace.Member.id` |
| provider | enum CredentialProvider | Required, default `Local` | `Local`, `Federated` (future) |
| password_hash | varchar(255) | Required when provider=Local | Argon2id hash |
| federated_subject | varchar(255) | Required when provider=Federated | Subject claim from IdP |
| created_at | timestamptz | Required, Auto | |
| updated_at | timestamptz | Required, Auto | |

---

### RefreshToken

> *Module: Identity — Rotated refresh tokens for the JWT flow.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | |
| member_id | UUID | Required | Cross-module reference to `Workspace.Member.id` |
| token_hash | varchar(255) | Required, unique | SHA-256 of the token |
| issued_at | timestamptz | Required | |
| expires_at | timestamptz | Required | |
| revoked_at | timestamptz | Nullable | Set when rotated or explicitly revoked |
| replaced_by_token_id | UUID | Nullable, FK → RefreshToken.id | Rotation chain |

---

### ExecutorRegistration

> *Module: ExecutorRegistry — A registered lifecycle executor available to DevHub.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | |
| key | varchar(60) | Required, unique | Stable identifier (e.g. `feature-delivery-v1`) |
| display_name | varchar(120) | Required | |
| base_url | varchar(500) | Required | Executor's HTTP base URL |
| credentials_ref | varchar(120) | Required | Reference to env-var/secret holding the executor credentials; **never stored as a literal** |
| status | enum ExecutorStatus | Required, default `Active` | `Active`, `Paused`, `Retired` |
| protocol | varchar(20) | Required, default `"devhub"` | FEAT-010: selects the `IExecutorHttpClient` implementation. `"devhub"` for DevHub-native protocol (FakeExecutor + legacy); `"orchestrator"` for the carestechs-agent-orchestrator's `/api/v1/runs` API. |
| created_at | timestamptz | Required, Auto | |
| updated_at | timestamptz | Required, Auto | |
| deleted_at | timestamptz | Nullable | |

**Business Rules:**
- `credentials_ref` is **only** a reference. The literal secret is read from configuration at request time.
- A `Retired` executor still serves fetch-state and stream reads for historical work items but rejects new work.

---

### ExecutorBinding

> *Module: ExecutorRegistry — Binds a project type to an executor.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | |
| project_type | varchar(60) | Required | Matches `Project.project_type` |
| executor_id | UUID | Required, FK → ExecutorRegistration.id | |
| created_at | timestamptz | Required, Auto | |
| deleted_at | timestamptz | Nullable | |

**Indexes:**
- Unique on `(project_type)` where `deleted_at IS NULL` (one active binding per project type).

---

### CheckpointContract

> *Module: ExecutorRegistry — Declared shape of a checkpoint an executor exposes. Authorization in WorkItems consults this to learn which role a given checkpoint requires.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | |
| executor_id | UUID | Required, FK → ExecutorRegistration.id | |
| checkpoint_key | varchar(60) | Required | Stable identifier the executor uses for this checkpoint |
| display_name | varchar(120) | Required | UI label |
| required_role_key | varchar(60) | Required | Matches `Role.key`; the role required to signal this checkpoint |
| allowed_outcomes | text[] | Required | e.g. `["approve","reject","revise"]` |
| per_task | boolean | Required, default `false` | When `true`, pending actions on this checkpoint are keyed per task (`PendingActionSignal.task_id`); the executor advances `WorkItem.current_task_id` between pauses. FEAT-009. |
| created_at | timestamptz | Required, Auto | |

**Indexes:**
- Unique on `(executor_id, checkpoint_key)`.

---

### WorkItem

> *Module: WorkItems — A project-scoped index entry for one execution of one lifecycle. Holds just enough state to render lists and route actions; full state lives in the executor.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | |
| project_id | UUID | Required | Cross-module reference to `Workspace.Project.id` |
| executor_id | UUID | Required | Cross-module reference to `ExecutorRegistry.ExecutorRegistration.id` |
| executor_correlation_marker | varchar(120) | Required, unique per executor | DevHub-issued id passed to the executor on every command; how we recognise this run later |
| title | varchar(255) | Required | Human-readable title for the work |
| current_status | varchar(60) | Required | Latest snapshot (`Running`, `WaitingOnCheckpoint`, `Completed`, `Failed`, `Cancelled`) — kept fresh by fetch + stream wrappers, not authoritative |
| current_checkpoint_key | varchar(60) | Nullable | Set when `current_status = WaitingOnCheckpoint` |
| current_task_id | varchar(60) | Nullable | Identifier of the task the executor is currently on, when the active checkpoint is `per_task=true`. Cached from the executor's response on every transition; the executor's memory is authoritative. FEAT-009. |
| work_branch | varchar(200) | Optional | Optional per-work-item override of the project's `default_branch`. Forwarded as `intake.codeSource.workBranch` on start; omitted (not sent as `null`) when unset. Same validation rules as `default_branch`. |
| executor_run_id | UUID | Nullable | The orchestrator's `Run.id` for this work item (FEAT-010). Populated by `OrchestratorExecutorClient.StartAsync` when the bound executor speaks the `orchestrator` protocol; null for `devhub`-protocol executors. |
| created_at | timestamptz | Required, Auto | |
| created_by_member_id | UUID | Required | The member who started this work |
| updated_at | timestamptz | Required, Auto | |

**Indexes:**
- Index on `(project_id, current_status)`.
- Unique on `(executor_id, executor_correlation_marker)`.

**Business Rules:**
- `current_status` and `current_checkpoint_key` are a **cache**. The executor is authoritative. Reads MUST NOT be blocked on this cache; they fall through to the executor.
- A WorkItem is never deleted. Cancellation is a status transition.

---

### CheckpointSignal

> *Module: WorkItems — A record of every signal sent to an executor checkpoint, captured before forward.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | |
| work_item_id | UUID | Required, FK → WorkItem.id | |
| checkpoint_key | varchar(60) | Required | Matches `CheckpointContract.checkpoint_key` |
| outcome | varchar(60) | Required | One of the contract's `allowed_outcomes` |
| payload_json | jsonb | Optional | Contract-shaped payload (review notes, diff comments) |
| signaled_by_member_id | UUID | Required | The acting member |
| signaled_at | timestamptz | Required, Auto | |
| executor_response_status | int | Nullable | HTTP status returned by the executor |
| executor_response_at | timestamptz | Nullable | When the response came back |

**Indexes:**
- Index on `(work_item_id, signaled_at)`.

---

### AuditEntry

> *Module: Audit — Append-only record of every DevHub-mediated action (grants AND denies). Never updated, never deleted.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | |
| occurred_at | timestamptz | Required, Auto | |
| acting_member_id | UUID | Nullable | Cross-module reference to `Workspace.Member.id`; nullable for system actions |
| project_id | UUID | Nullable | Cross-module reference to `Workspace.Project.id`; nullable for workspace-level actions (e.g. team creation) |
| target_type | varchar(60) | Required | e.g. `WorkItem`, `Project`, `Team`, `CheckpointSignal`, `ExecutorRegistration` |
| target_id | UUID | Nullable | Id of the target |
| action | varchar(60) | Required | e.g. `workitem:start`, `checkpoint:signal`, `team:create`, `executor:register` |
| outcome | enum AuditOutcome | Required | `Granted`, `Denied`, `Failed` |
| reason | text | Nullable | Short reason on Denied/Failed (e.g. "member not on project") |
| details_json | jsonb | Optional | Contextual data (e.g. denied check parameters) |

**Indexes:**
- Index on `(project_id, occurred_at desc)`.
- Index on `(acting_member_id, occurred_at desc)`.
- Index on `(outcome, occurred_at desc)`.

**Business Rules:**
- INSERT only. UPDATE and DELETE are forbidden at the DB level (enforced by trigger or by application contract; v1 chooses application contract).

---

### PendingActionSignal

> *Module: Notifications — One row per (member, project, work-item, checkpoint) currently waiting on that member's role. The set is recomputed on relevant WorkItem transitions.*

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| id | UUID | PK | |
| member_id | UUID | Required | Cross-module reference to `Workspace.Member.id` |
| project_id | UUID | Required | Cross-module reference to `Workspace.Project.id` |
| work_item_id | UUID | Required | Cross-module reference to `WorkItems.WorkItem.id` |
| checkpoint_key | varchar(60) | Required | Matches `CheckpointContract.checkpoint_key` |
| task_id | varchar(60) | Nullable | Set when the active contract is `per_task=true` (FEAT-009). Distinguishes per-task pending rows so multi-task work items show one row per task instead of one row per checkpoint. |
| created_at | timestamptz | Required, Auto | When the signal was raised |
| dismissed_at | timestamptz | Nullable | When the checkpoint was resolved (any outcome) — kept briefly for UI fade |

**Indexes:**
- Unique on `(member_id, work_item_id, checkpoint_key, COALESCE(task_id, '<root>'))` where `dismissed_at IS NULL` (FEAT-009 — folds NULL `task_id`s into a sentinel so legacy / non-per-task rows still collide as one, while distinct task ids coexist).
- Non-unique index on `(member_id, work_item_id, checkpoint_key)` (for query patterns that don't care about task discrimination).
- Index on `(member_id, project_id)` where `dismissed_at IS NULL`.

## Relationships

### One-to-Many

| Parent Entity | Child Entity | Foreign Key | Cascade Behavior |
|---------------|-------------|-------------|------------------|
| Team | Project | `owning_team_id` on Project | Restrict delete (a team owning projects cannot be deleted) |
| Project | ProjectMembership | `project_id` on ProjectMembership | Soft cascade (deleting a project soft-deletes memberships) |
| Member | ProjectMembership | `member_id` on ProjectMembership | Soft cascade |
| ProjectMembership | RoleAssignment | `project_membership_id` on RoleAssignment | Soft cascade |
| Role | RoleAssignment | `role_id` on RoleAssignment | Restrict delete (a role with assignments cannot be deleted) |
| ExecutorRegistration | CheckpointContract | `executor_id` on CheckpointContract | Soft cascade |
| ExecutorRegistration | ExecutorBinding | `executor_id` on ExecutorBinding | Soft cascade |
| WorkItem | CheckpointSignal | `work_item_id` on CheckpointSignal | Restrict (CheckpointSignal is historical evidence) |

### Many-to-Many

| Entity A | Entity B | Join Table | Additional Fields |
|----------|----------|-----------|-------------------|
| Member | Project | ProjectMembership | (audit fields) |
| ProjectMembership | Role | RoleAssignment | (audit fields) |

### Cross-Module References (ID only)

| Source Entity (Module) | Target Entity (Module) | Field | Purpose |
|----------------------|----------------------|-------|---------|
| IdentityCredential (Identity) | Member (Workspace) | `member_id` | Login resolves to Member |
| RefreshToken (Identity) | Member (Workspace) | `member_id` | Refresh issuance |
| WorkItem (WorkItems) | Project (Workspace) | `project_id` | Project scoping |
| WorkItem (WorkItems) | ExecutorRegistration (ExecutorRegistry) | `executor_id` | Routing |
| WorkItem (WorkItems) | Member (Workspace) | `created_by_member_id` | "Who started this" |
| CheckpointSignal (WorkItems) | Member (Workspace) | `signaled_by_member_id` | Acting member |
| AuditEntry (Audit) | Member (Workspace) | `acting_member_id` | Audit subject |
| AuditEntry (Audit) | Project (Workspace) | `project_id` | Audit scope |
| PendingActionSignal (Notifications) | Member, Project, WorkItem (multiple) | `member_id`, `project_id`, `work_item_id` | Targeting |
| ProjectMembership.created_by_member_id | Member (Workspace, same module) | self | In-module FK |

## Enums

### MemberStatus

> *Used by: Member.status*

| Value | Description |
|-------|-------------|
| Active | Normal authenticated state |
| Suspended | Cannot authenticate; existing memberships preserved |
| Invited | Created but not yet onboarded |

### CredentialProvider

> *Used by: IdentityCredential.provider*

| Value | Description |
|-------|-------------|
| Local | Email + password in this system |
| Federated | External IdP (deferred — open question in stakeholder def) |

### ExecutorStatus

> *Used by: ExecutorRegistration.status*

| Value | Description |
|-------|-------------|
| Active | Routable; accepts new work |
| Paused | Existing work continues; no new work routed here |
| Retired | Read-only; historical work items still viewable |

### AuditOutcome

> *Used by: AuditEntry.outcome*

| Value | Description |
|-------|-------------|
| Granted | Authorization succeeded and the action was forwarded/applied |
| Denied | Authorization failed; the action did not reach the executor |
| Failed | Authorized but the executor (or local work) returned an error |

### WorkItemStatus (string, not a CLR enum — values come from executors)

> *Used by: WorkItem.current_status*

Conventional values: `Running`, `WaitingOnCheckpoint`, `Completed`, `Failed`, `Cancelled`. Stored as `varchar(60)` because executors may extend the set; DevHub displays whatever the executor reports.

## Database Conventions

| Convention | Rule | Example |
|------------|------|---------|
| Table naming | snake_case, plural, per-module schema | `workspace.projects`, `audit.audit_entries` |
| Column naming | snake_case | `created_at`, `executor_correlation_marker` |
| Primary keys | UUID, column named `id`, generated in C# | `id uuid PK` |
| Timestamps | `timestamptz`, always UTC; `created_at` + `updated_at` on every mutable entity | `created_at timestamptz NOT NULL` |
| Soft delete | `deleted_at timestamptz NULL`; queries filter `deleted_at IS NULL` by default | applied to all Workspace + ExecutorRegistry primitives |
| Audit immutability | INSERT only on `audit.audit_entries`; no UPDATE/DELETE | enforced by application contract |
| Migrations | Per-module (`dotnet ef migrations add ... --project src/DevHub.Modules.<Name>`) | each module owns its own migration history table |

## AI Task Generation Notes

- **Module boundaries**: Every data-access task must target the correct module's DbContext. Never load entities across DbContexts; resolve by ID via `DevHub.Contracts` interfaces.
- **Field completeness**: Generated entity classes must include all fields defined here, including the soft-delete `deleted_at` column where applicable.
- **Authorization data is the spine.** Any feature that touches end-user data must compose with `ProjectMembership` + `RoleAssignment` for its authorization check.
- **Audit on every mutation.** Every DevHub-mediated mutation must write an `AuditEntry` in the same transaction as the change.
- **Lifecycle state is not ours.** Do not propose adding "full execution graph" fields to `WorkItem`. DevHub caches only `current_status` + `current_checkpoint_key`.

## Changelog

- **2026-05-15** — Initial data model compiled from DevHub stakeholder definition. Defines 12 entities across 6 modules (Workspace, Identity, ExecutorRegistry, WorkItems, Audit, Notifications), the soft-delete + append-only-audit policy, and the ID-only cross-module reference contract.
- **2026-05-17 (FEAT-008 / T-055)** — `Project` gained optional `repo` (varchar 140) and `default_branch` (varchar 200). `WorkItem` gained optional `work_branch` (varchar 200). All three nullable; existing rows survive the migration. Validation rules mirror the orchestrator's `intake.codeSource` schema; values are forwarded on work-item start.
- **2026-05-17 (FEAT-009 / T-064)** — `CheckpointContract` gained `per_task` (bool, default `false`). `WorkItem` gained `current_task_id` (nullable varchar 60). `PendingActionSignal` gained `task_id` (nullable varchar 60); active-row uniqueness rewritten to `(member_id, work_item_id, checkpoint_key, COALESCE(task_id, '<root>'))` where `dismissed_at IS NULL`. Existing rows survive (legacy `task_id = NULL` rows collide as a single sentinel — same behavior as today).
- **2026-05-17 (FEAT-010 / T-084)** — `WorkItem` gained nullable `executor_run_id` (uuid). `ExecutorRegistration` gained `protocol` (varchar 20, default `"devhub"`). Drives the `IExecutorHttpClient` implementation selection (FEAT-010). Existing executor rows backfill to `"devhub"` so all legacy flows continue to use the existing `ExecutorHttpClient`.
