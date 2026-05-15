# Feature Brief: FEAT-002 — Workspace Primitives (Teams, Members, Projects, Memberships)

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-002 |
| **Name** | Workspace Primitives — Teams, Members, Projects, Memberships, Roles |
| **Target Version** | v1 |
| **Status** | Not Started |
| **Priority** | Critical |
| **Requested By** | Architecture (Stakeholder Scope: Workspace primitives) |
| **Date Created** | 2026-05-15 |

## 2. User Story

**As a** workspace operator, **I want to** create teams, invite members, create projects, and grant members project memberships with roles, **so that** later checkpoint authorization can resolve `(member, role, project)` for every end-user action.

## 3. Goal

CRUD for every workspace primitive, the `IProjectAuthorizationService` published in `DevHub.Contracts`, and the admin screens to drive it all — sufficient to satisfy Success Criterion #1 ("≥3 distinct projects, each owned by a distinct team").

## 4. Feature Scope

### 4.1 Included

- All entities in `data-model.md` for the **Workspace** module: Team, Member, Project, Role, RoleAssignment, ProjectMembership.
- All endpoints under `/api/teams`, `/api/members`, `/api/projects`, `/api/projects/{id}/memberships`, `/api/roles` (see `api-spec.md`).
- `IProjectAuthorizationService.AuthorizeAsync(member, projectId, action, requiredRoleKey?)` exposed via `DevHub.Contracts`.
- Soft delete with `deleted_at` on every workspace primitive.
- Audit entry written on every mutation.
- Admin UI: Teams, Members, Project memberships screens (see `ui-specification.md` §9–11).
- Project list + Project home screens for members.

### 4.2 Excluded

- Federated identity (later).
- Bulk import of members (future improvement).
- Per-project custom roles (only workspace-global roles in v1).

## 5. Acceptance Criteria

- **AC-1:** Operator can create a team, invite a member, create a project, and add the member to the project with a role — entirely from the SPA.
- **AC-2:** A non-operator member without `ProjectMembership` on project P receives `403 /probs/forbidden` from every project-scoped endpoint for P; the deny is audited.
- **AC-3:** Three distinct projects, each owned by a distinct team, can run concurrently with distinct memberships. (Smoke test.)
- **AC-4:** Soft-deleting a project hides it from list endpoints; audit log entries remain.
- **AC-5:** `IProjectAuthorizationService` denies suspended members, soft-deleted members, and members lacking the required role.
- **AC-6:** Every mutation has at least one deny-path test and one grant-path test.

## 6. Key Entities and Business Rules

| Entity | Role | Rules |
|--------|------|-------|
| Team | Owns projects | Cannot delete if it owns non-deleted projects |
| Member | Authorization subject | Suspended/deleted → all checks deny |
| Project | Scope unit | Must bind to a known `ExecutorBinding` (validated in FEAT-003) |
| ProjectMembership | (member, project) pair | Unique per `(project_id, member_id)` non-deleted |
| RoleAssignment | (membership, role) | Unique per `(membership_id, role_id)` non-deleted |
| Role | Capability set | `operator` is `is_system = true`; cannot be deleted |

## 7. API Impact

All `/api/teams/*`, `/api/members/*`, `/api/projects/*` (excluding `/work-items/*`), `/api/roles` endpoints (see `api-spec.md`).

## 8. UI Impact

| Screen | Status | Description |
|--------|--------|-------------|
| Project list (`/projects`) | New | Cards of projects in scope |
| Project home (`/projects/:slug`) | New (without work-items table — added in FEAT-004) | Header + memberships entry point |
| Teams admin | New | Standard CRUD table |
| Members admin | New | Standard CRUD table |
| Project memberships admin | New | Member + role assignment |

## 9. Edge Cases

- Adding a member already in the project → 409.
- Removing the last operator → 409, "at least one operator must remain."
- Project name/slug uniqueness collisions on soft-deleted records — uniqueness applies only to non-deleted rows.
- Concurrent role assignment edits — last write wins, audit entry shows both.

## 10. Constraints

- All cross-module references are by ID, via `DevHub.Contracts`. No navigation properties from Identity or other modules into Workspace.
- `IProjectAuthorizationService` is the single check used everywhere — never re-implement.

## 11. Motivation and Priority Justification

**Motivation:** Org primitives are the spine. Authorization, audit, and the executor façade all consume them.
**Impact if delayed:** FEAT-004 (the façade) cannot ship without authorization data.
**Dependencies on this feature:** FEAT-003, FEAT-004, FEAT-005, FEAT-006.

## 12. Traceability

| Reference | Link |
|-----------|------|
| **Persona** | `docs/personas/primary-user.md` |
| **Stakeholder Scope Item** | "Workspace primitives"; "Identity and end-to-end authorization" |
| **Success Metric** | "Concurrent projects ≥3"; "Authorization correctness 100%" |
| **Related Work Items** | Blocked by FEAT-001. Blocks FEAT-003, FEAT-004, FEAT-005, FEAT-006. |
