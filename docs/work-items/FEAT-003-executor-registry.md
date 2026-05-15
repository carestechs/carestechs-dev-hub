# Feature Brief: FEAT-003 — Executor Registry & Bindings

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-003 |
| **Name** | Executor Registry & Bindings |
| **Target Version** | v1 |
| **Status** | Not Started |
| **Priority** | Critical |
| **Requested By** | Architecture (Stakeholder Scope: Lifecycle executor registry) |
| **Date Created** | 2026-05-15 |

## 2. User Story

**As an** operator, **I want to** register one or more lifecycle executors with their checkpoint contracts and bind project types to them, **so that** any new project of a known type routes to the right executor with zero portfolio code changes.

## 3. Goal

Configuration-only onboarding of a second executor of a known shape (Success Criterion #3). The `IExecutorRouter` resolves `Project → ExecutorRegistration` deterministically; `CheckpointContract` drives the role-required check used by `IProjectAuthorizationService` for checkpoint signals.

## 4. Feature Scope

### 4.1 Included

- Entities: ExecutorRegistration, ExecutorBinding, CheckpointContract.
- Endpoints under `/api/admin/executors/*` and `/api/admin/executor-bindings/*`.
- `IExecutorRouter.ResolveAsync(projectId): ExecutorRegistration` published via `Portfolio.Contracts`.
- Operator UI: Executors admin, Executor bindings admin (see `ui-specification.md` §12–13).
- `credentialsRef` indirection — secrets read from env vars at request time; never stored, never returned.

### 4.2 Excluded

- Discovering executors via service registry (manual registration only in v1).
- Per-environment overrides beyond env-var `credentialsRef` resolution.
- Re-binding an in-flight project to a different executor (open question for v2).

## 5. Acceptance Criteria

- **AC-1:** Registering a second executor of a known shape (same DTO + checkpoint contract format) requires **zero code changes**.
- **AC-2:** Creating a project whose `projectType` has no active binding returns 409 with `/probs/conflict`, "no executor bound for this project type."
- **AC-3:** `IExecutorRouter.ResolveAsync` returns the bound executor for a project, honoring `ExecutorStatus`: `Paused` blocks new work, `Retired` blocks new work but allows reads.
- **AC-4:** `credentialsRef` values are never returned by any API and never logged.
- **AC-5:** Admin UI lists executors with their `CheckpointContract`s, showing per-contract `requiredRoleKey` (which `Role.key` must hold to signal).

## 6. Key Entities and Business Rules

| Entity | Role | Rules |
|--------|------|-------|
| ExecutorRegistration | A known executor | `credentialsRef` is a reference only |
| ExecutorBinding | (projectType → executor) | One active per projectType |
| CheckpointContract | Shape of a checkpoint | `requiredRoleKey` MUST match an existing `Role.key` at registration |

## 7. API Impact

All `/api/admin/executors/*` and `/api/admin/executor-bindings/*` endpoints. See `api-spec.md` § Executor Registry.

## 8. UI Impact

| Screen | Status | Description |
|--------|--------|-------------|
| Executors admin | New | List + register + edit; surface contracts and status |
| Executor bindings admin | New | One active binding per project type |

## 9. Edge Cases

- Registering an executor whose `requiredRoleKey` does not exist → 400.
- Binding to a `projectType` already bound → 409 (delete the old binding or use `PATCH`).
- Deleting an `ExecutorRegistration` with active bindings → 409.
- `Retired` executor with in-flight work items → reads and streams continue; new work blocked.

## 10. Constraints

- Operators only.
- `credentialsRef` must never leak (no logging, no JSON return).
- All authorization for checkpoint signals MUST consult `CheckpointContract.requiredRoleKey` — never hard-code a role.

## 11. Motivation and Priority Justification

**Motivation:** The portfolio's value proposition is being executor-agnostic by construction. The registry is what makes that claim true.
**Impact if delayed:** Cannot onboard a real executor; FEAT-004 cannot route.
**Dependencies on this feature:** FEAT-004, FEAT-005.

## 12. Traceability

| Reference | Link |
|-----------|------|
| **Persona** | (Operator — not the primary persona) |
| **Stakeholder Scope Item** | "Lifecycle executor registry"; "Executor-agnostic by construction" |
| **Success Metric** | "Executor independence — configuration diff only" |
| **Related Work Items** | Blocked by FEAT-001, FEAT-002. Blocks FEAT-004, FEAT-005. |
