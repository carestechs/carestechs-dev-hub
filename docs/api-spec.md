# API Specification

## Overview

DevHub API is the **single front door** for every end-user action and for operator administration of executors. All endpoints live under `/api/`, are served by `DevHub.Api` (the thin host), and are organized into per-module controllers. Six modules expose endpoints: Identity, Workspace, ExecutorRegistry, WorkItems (the façade), Audit, and Notifications.

Every endpoint that wraps a lifecycle executor action **MUST authorize `(member, role, project, target)` at DevHub boundary before forwarding**. Denied requests never reach the executor.

### Key API Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Base path | `/api/` (reverse-proxied by nginx in prod) | Single origin for SPA; simpler CORS, simpler cookies |
| Versioning | None in v1 path; breaking changes go through a feature flag | Single-tenant per org; only one client (the bundled SPA) |
| Auth mechanism | JWT Bearer (short-lived access) + httpOnly refresh cookie | Stateless API; SPA-friendly; no per-executor credentials in the browser |
| Response envelope | `{ "data": ..., "meta": ... }` on success | Uniform shape; pagination metadata has a home |
| Error format | RFC 7807 Problem Details (`application/problem+json`) | Standard, structured, machine + human readable |
| Pagination | Offset (`page`, `pageSize`, `sortBy`, `sortDir`) in query string; metadata in envelope | Simple, predictable, sufficient for v1 list sizes |
| Streaming | Server-Sent Events (`text/event-stream`) — proxied pass-through from the executor | Native browser support; no buffering, no transformation in the middle |
| Casing | camelCase JSON; snake_case DB; PascalCase C# (auto via `System.Text.Json` default + EF naming convention) | One canonical translation per layer |
| Identifiers | UUID v4 in path params and DTOs | End-to-end identity strategy |
| URLs | kebab-case, plural collections, nested by project where applicable | `/api/projects/{projectId}/work-items` |

## Common Conventions

### Response Envelope (success)

```json
{
  "data": { },
  "meta": {
    "totalCount": 0,
    "page": 1,
    "pageSize": 20,
    "sortBy": "createdAt",
    "sortDir": "desc"
  }
}
```

`meta` is omitted on non-list responses except where additional context (e.g. `correlationId`) is useful.

### Error Response (RFC 7807)

```json
{
  "type": "https://carestechs.example/probs/forbidden",
  "title": "Forbidden",
  "status": 403,
  "detail": "Member is not a participant in this project.",
  "instance": "/api/projects/3f.../work-items/9a.../checkpoints/review/signal",
  "correlationId": "01J...",
  "errors": {
    "checkRequired": ["role:approver on project 3f..."]
  }
}
```

Standard `type` URIs: `/probs/validation`, `/probs/forbidden`, `/probs/not-found`, `/probs/conflict`, `/probs/executor-failure`, `/probs/internal`.

### Authentication

- **Mechanism:** JWT Bearer token in `Authorization` header for all `/api/*` endpoints except `POST /api/auth/login`, `POST /api/auth/refresh`, and `GET /health`.
- **Token format:** `Authorization: Bearer <access-token>`.
- **Refresh:** httpOnly, SameSite=Lax cookie set by `/api/auth/login`; rotated on every `/api/auth/refresh`.

### Authorization

Every endpoint declares a **required check** in the `Roles` row. Conventional check syntax:

- `Public` — no auth required.
- `Authenticated` — any logged-in member.
- `Project:<roleKey>` — member is on the project (path param `projectId`) and holds the named role.
- `Project:any` — member is on the project (any role) — used for reads scoped to membership.
- `System:operator` — global `operator` role (workspace admin).

Authorization is the first non-validation line of the controller action and writes to the audit log on both grant and deny.

### Pagination

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| page | int | 1 | 1-based page number |
| pageSize | int | 20 | 1–100 |
| sortBy | string | varies per endpoint | Allowed values listed per endpoint |
| sortDir | string | `desc` | `asc` or `desc` |

## Endpoints by Module

### Identity

#### Authentication

##### POST /api/auth/login

> *Exchange credentials for an access token (response) + refresh token (httpOnly cookie).*

| Attribute | Value |
|-----------|-------|
| **Auth** | Public |
| **Roles** | — |

**Request Body:**

```json
{
  "email": "string — email of the member",
  "password": "string — plaintext over TLS"
}
```

**Response (200 OK):**

```json
{
  "data": {
    "accessToken": "string — JWT",
    "expiresAt": "string — ISO-8601",
    "member": {
      "id": "uuid",
      "displayName": "string",
      "email": "string"
    }
  }
}
```

Sets `Set-Cookie: refresh=<token>; HttpOnly; Secure; SameSite=Lax; Path=/api/auth`.

| Code | Condition |
|------|-----------|
| 200 | Success |
| 400 | Validation error |
| 401 | Bad credentials |
| 403 | Member suspended |

##### POST /api/auth/refresh

> *Rotate the refresh cookie and return a fresh access token.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Public (consumes refresh cookie) |
| **Roles** | — |

**Response (200 OK):**

```json
{
  "data": {
    "accessToken": "string",
    "expiresAt": "string — ISO-8601"
  }
}
```

| Code | Condition |
|------|-----------|
| 200 | Success |
| 401 | Refresh cookie missing or invalid |

##### POST /api/auth/logout

> *Revoke the current refresh token chain and clear the cookie.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Authenticated |
| **Roles** | — |

**Response:** 204 No Content.

##### GET /api/auth/me

> *Return the current member and their (project, roles) map.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Authenticated |
| **Roles** | — |

**Response (200 OK):**

```json
{
  "data": {
    "member": { "id": "uuid", "displayName": "string", "email": "string" },
    "memberships": [
      { "projectId": "uuid", "projectSlug": "string", "roles": ["reviewer", "approver"] }
    ]
  }
}
```

---

### Workspace

#### Teams

##### GET /api/teams

> *List teams in the workspace.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Authenticated |
| **Roles** | Authenticated |

Query: `page`, `pageSize`, `sortBy` ∈ {`name`, `createdAt`}, `sortDir`, `q` (free-text search on `name`).

**Response (200 OK):** envelope with `data: TeamDto[]`.

##### POST /api/teams

> *Create a team.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | System:operator |

**Request Body:**

```json
{
  "name": "string — required, unique",
  "description": "string — optional"
}
```

**Response (201 Created):** envelope with `data: TeamDto`.

##### GET /api/teams/{teamId}

##### PATCH /api/teams/{teamId}

##### DELETE /api/teams/{teamId} (soft delete)

> Standard CRUD; `System:operator`. Returns 409 on DELETE if the team owns non-deleted projects.

#### Members

##### GET /api/members

##### POST /api/members (invite)

##### GET /api/members/{memberId}

##### PATCH /api/members/{memberId} (status, displayName)

##### DELETE /api/members/{memberId} (soft delete)

> All `System:operator`. Standard CRUD shapes.

#### Projects

##### GET /api/projects

> *List projects the caller can see (their memberships + operator-visible).*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | Authenticated (scoped to memberships); operators see all |

Query: `page`, `pageSize`, `sortBy` ∈ {`name`, `createdAt`}, `sortDir`, `teamId`, `projectType`, `status` ∈ {`active`, `archived`}.

**Response (200 OK):** envelope with `data: ProjectDto[]`.

##### POST /api/projects

> *Create a project. `projectType` must resolve to an existing `ExecutorBinding`.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | System:operator |

**Request Body:**

```json
{
  "name": "string",
  "slug": "string — kebab-case",
  "projectType": "string",
  "owningTeamId": "uuid",
  "description": "string — optional",
  "repo": "string — optional, owner/name (FEAT-008)",
  "defaultBranch": "string — optional, git branch shorthand (FEAT-008)"
}
```

`repo` and `defaultBranch` are forwarded to the lifecycle executor as `intake.codeSource.{repo, baseBranch}` on every work-item start. Both are validated at the DevHub boundary with rules that mirror the upstream orchestrator's `intake.codeSource` schema:
- `repo` matches `^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$`; no scheme prefix, no `.git` suffix.
- `defaultBranch` has no whitespace, no leading `/`, no `..`, no ASCII control chars.

`ProjectDto` carries both fields in every response envelope (`null` when unset).

| Code | Condition |
|------|-----------|
| 201 | Created |
| 400 | Validation error (includes `repo` / `defaultBranch` rule violations) |
| 409 | Slug/name collision; `projectType` has no binding |

##### GET /api/projects/{projectId}

##### PATCH /api/projects/{projectId}

Accepts any subset of `{ name, description, projectType, repo, defaultBranch }`. Fields not present in the request body are left unchanged. `repo` and `defaultBranch` are validated with the same rules as on create; rejected values produce `400` with no DB write and a `Denied` audit entry. Changes to `repo` / `defaultBranch` are recorded in the `project:update` audit entry's `details` with `repoBefore` / `repoAfter` / `defaultBranchBefore` / `defaultBranchAfter` keys (only the keys for fields that actually changed).

##### DELETE /api/projects/{projectId} (soft delete)

> Reads: `Project:any`. Writes: `System:operator`. DELETE soft-deletes the project and its memberships.

#### Project Memberships

##### GET /api/projects/{projectId}/memberships

##### POST /api/projects/{projectId}/memberships

> *Add a member to a project with one or more role assignments.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | System:operator |

**Request Body:**

```json
{
  "memberId": "uuid",
  "roleKeys": ["reviewer", "approver"]
}
```

##### PATCH /api/projects/{projectId}/memberships/{membershipId}

> *Replace the role assignments on a membership.* `System:operator`.

##### DELETE /api/projects/{projectId}/memberships/{membershipId}

> Soft delete; `System:operator`.

#### Roles (lookup)

##### GET /api/roles

> *Workspace-global list of roles.* Authenticated.

---

### Executor Registry

> Operator-only. Adding a second executor of a known shape requires **configuration only** — no code change.

#### Executor Registrations

##### GET /api/admin/executors

##### POST /api/admin/executors

> *Register a new lifecycle executor.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | System:operator |

**Request Body:**

```json
{
  "key": "string — e.g. feature-delivery-v1",
  "displayName": "string",
  "baseUrl": "string — URL",
  "credentialsRef": "string — env-var/secret reference; never a literal",
  "checkpointContracts": [
    {
      "checkpointKey": "string",
      "displayName": "string",
      "requiredRoleKey": "string — matches Role.key",
      "allowedOutcomes": ["approve", "reject", "revise"]
    }
  ]
}
```

| Code | Condition |
|------|-----------|
| 201 | Created |
| 400 | Validation error (e.g. unknown `requiredRoleKey`) |
| 409 | Duplicate `key` |

##### GET /api/admin/executors/{id}

##### PATCH /api/admin/executors/{id} (status, displayName, baseUrl, credentialsRef)

##### POST /api/admin/executors/{id}/checkpoint-contracts (append or replace)

##### DELETE /api/admin/executors/{id} (soft, must have no Active bindings)

#### Executor Bindings

##### GET /api/admin/executor-bindings

##### POST /api/admin/executor-bindings

> *Bind a `projectType` to an executor. One active binding per `projectType`.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | System:operator |

**Request Body:**

```json
{
  "projectType": "string",
  "executorId": "uuid"
}
```

##### DELETE /api/admin/executor-bindings/{id} (soft)

---

### Work Items (the façade)

> DevHub's only path to lifecycle executors. Every endpoint here authorizes before forwarding.

#### Work Item Index

##### GET /api/projects/{projectId}/work-items

> *List work items in a project.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | Project:any |

Query: `page`, `pageSize`, `sortBy` ∈ {`createdAt`, `updatedAt`, `title`}, `sortDir`, `status` (filter), `waitingOnMe` (boolean — restricts to items with a `PendingActionSignal` for the caller).

**Response (200 OK):** envelope with `data: WorkItemSummaryDto[]`.

##### POST /api/projects/{projectId}/work-items

> *Start a new work item. Forwards a "start" command to the bound executor and stores the index entry.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | Project:<role granted to start this project type> (resolved from `CheckpointContract.requiredRoleKey` for the executor's initial entry) |

**Request Body:**

```json
{
  "title": "string",
  "input": { "any": "executor-shaped payload" },
  "workBranch": "string — optional, per-work-item override of the project's default branch (FEAT-008)"
}
```

`workBranch`, when set, is forwarded to the executor as `intake.codeSource.workBranch` (T-059) and stored on the WorkItem row. Validated at the boundary against the same branch-shorthand rules as `defaultBranch` (no whitespace, no leading `/`, no `..`, no control chars).

**Response (201 Created):** envelope with `data: WorkItemDto`. `WorkItemDto.workBranch` echoes whatever was persisted (`null` when not set on the request).

| Code | Condition |
|------|-----------|
| 201 | Created |
| 400 | Validation error (includes `workBranch` rule violations) |
| 403 | Authorization failed |
| 409 | Project not bound to any executor |
| 502 | Executor refused or unreachable (problem-detail includes executor id + correlationId) |

##### PATCH /api/projects/{projectId}/work-items/{workItemId}

> *Update a work item. v1 surface is intentionally minimal: only `workBranch` is updatable. The slot exists for later FEATs (e.g. PR linkage callbacks) without further schema migration.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | System:operator |

**Request Body:**

```json
{
  "workBranch": "string | null | empty string"
}
```

Semantics:
- `null` (or property omitted) → leave the existing value unchanged.
- `""` (empty string) → clear the override; the work item falls back to the project's `defaultBranch` at display time.
- Any other string → validated and persisted.

The branch value forwarded to the executor was captured at start time and is **not re-forwarded** by this endpoint — an in-flight run does not learn about a branch edit. The audit entry (`workitem:update`, `Granted`) carries `workBranchBefore` / `workBranchAfter` in `details` when the value actually changes.

**Response (200 OK):** envelope with `data: WorkItemDto`. `ExecutorState` is `null` in this response (no executor refetch; call GET to refresh).

| Code | Condition |
|------|-----------|
| 200 | Updated (or no-op when `workBranch` matches existing) |
| 400 | Validation error |
| 403 | Authorization failed (non-operator) |
| 404 | Work item not found |
| 409 | Project has no executor bound (descriptor needed to build the response) |

##### GET /api/projects/{projectId}/work-items/{workItemId}

> *Get the latest snapshot from the executor (pass-through), enriched with DevHub metadata.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | Project:any |

**Response (200 OK):**

```json
{
  "data": {
    "id": "uuid",
    "projectId": "uuid",
    "title": "string",
    "executor": { "id": "uuid", "key": "string", "displayName": "string" },
    "executorCorrelationMarker": "string",
    "currentStatus": "string",
    "currentCheckpointKey": "string|null",
    "createdAt": "iso-8601",
    "createdBy": { "id": "uuid", "displayName": "string" },
    "executorState": { "any": "executor-shaped — opaque to DevHub" },
    "workBranch": "string | null — optional per-work-item branch override (FEAT-008)"
  }
}
```

#### Checkpoint Signals

##### GET /api/projects/{projectId}/work-items/{workItemId}/checkpoints/{checkpointKey}

> *Inspect the contract + the current state of a checkpoint waiting for action.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | Project:any |

##### POST /api/projects/{projectId}/work-items/{workItemId}/checkpoints/{checkpointKey}/signal

> *Send a checkpoint signal (approve/reject/revise/...) to the executor.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | Project:<requiredRoleKey from the CheckpointContract for this `checkpointKey`> |

**Request Body:**

```json
{
  "outcome": "string — must be in the contract's allowedOutcomes",
  "payload": { "any": "contract-shaped" }
}
```

**Response (200 OK):** envelope with `data: WorkItemDto` (updated).

| Code | Condition |
|------|-----------|
| 200 | Forwarded; executor accepted |
| 400 | Outcome not in `allowedOutcomes`; payload schema mismatch |
| 403 | Member is not on the project OR does not hold the required role |
| 404 | Work item / checkpoint not found |
| 409 | Checkpoint is not currently waiting (already resolved) |
| 502 | Executor failure (problem-detail includes executor id + correlationId) |

##### GET /api/projects/{projectId}/work-items/{workItemId}/signals

> *History of every signal sent on this work item. Project:any.*

#### Live Stream

##### GET /api/projects/{projectId}/work-items/{workItemId}/stream

> *Pass-through SSE stream of the executor's live trace.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required (authorized at connection time, then proxies bytes) |
| **Roles** | Project:any |
| **Content-Type** | `text/event-stream` |

Events are forwarded **byte-for-byte** from the executor with no buffering, batching, or transformation. On client disconnect the upstream connection is closed; no replay state is held in DevHub.

##### POST /api/projects/{projectId}/work-items/{workItemId}/cancel

> *Cancel a running work item.* `Project:<role from contract or System:operator>`.

---

### Audit

#### Audit Log

##### GET /api/projects/{projectId}/audit

> *Project-scoped audit log (grants, denies, executor failures).*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | Project:any (reads); operators see additional cross-project endpoints |

Query: `page`, `pageSize`, `sortBy` (default `occurredAt`), `sortDir` (default `desc`), `actingMemberId`, `targetType`, `action`, `outcome` ∈ {`Granted`, `Denied`, `Failed`}, `from`, `to` (ISO-8601).

##### GET /api/admin/audit

> *Cross-project audit log.* `System:operator`. Same filters plus `projectId`.

---

### Notifications

##### GET /api/notifications/pending

> *List checkpoints across all projects that are waiting on the caller's role.*

| Attribute | Value |
|-----------|-------|
| **Auth** | Required |
| **Roles** | Authenticated (scoped to caller) |

**Response (200 OK):**

```json
{
  "data": [
    {
      "projectId": "uuid",
      "projectSlug": "string",
      "workItemId": "uuid",
      "workItemTitle": "string",
      "checkpointKey": "string",
      "checkpointDisplayName": "string",
      "raisedAt": "iso-8601"
    }
  ]
}
```

##### GET /api/notifications/stream

> *SSE stream that pushes new pending-action signals to the caller in real time.*

`text/event-stream`; authenticated; no project param — scoped to the caller.

---

### Operations

##### GET /health

> *Liveness probe.* Public. Returns 200 with `{ "status": "ok", "checks": { "db": "up" } }`.

---

## Shared DTOs

### TeamDto

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| id | uuid | No | |
| name | string | No | |
| description | string | Yes | |
| projectCount | int | No | Count of non-deleted owned projects |
| createdAt | iso-8601 | No | |

### MemberDto

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| id | uuid | No | |
| displayName | string | No | |
| email | string | No | |
| status | enum | No | `Active`, `Suspended`, `Invited` |
| createdAt | iso-8601 | No | |

### ProjectDto

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| id | uuid | No | |
| name | string | No | |
| slug | string | No | |
| projectType | string | No | |
| owningTeam | `{ id, name }` | No | |
| description | string | Yes | |
| inFlightWorkItems | int | No | Count of work items not in a terminal status |
| createdAt | iso-8601 | No | |

### ProjectMembershipDto

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| id | uuid | No | |
| member | `{ id, displayName, email }` | No | |
| roles | string[] | No | Role keys |
| createdAt | iso-8601 | No | |

### ExecutorDto

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| id | uuid | No | |
| key | string | No | |
| displayName | string | No | |
| baseUrl | string | No | |
| status | enum | No | `Active`, `Paused`, `Retired` |
| checkpointContracts | `CheckpointContractDto[]` | No | |

### CheckpointContractDto

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| checkpointKey | string | No | |
| displayName | string | No | |
| requiredRoleKey | string | No | |
| allowedOutcomes | string[] | No | |

### WorkItemSummaryDto

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| id | uuid | No | |
| title | string | No | |
| currentStatus | string | No | |
| currentCheckpointKey | string | Yes | |
| executor | `{ id, key, displayName }` | No | |
| createdBy | `{ id, displayName }` | No | |
| createdAt | iso-8601 | No | |
| updatedAt | iso-8601 | No | |
| waitingOnMe | boolean | No | True if the caller has a `PendingActionSignal` on this item |
| workBranch | string | Yes | Per-work-item override of the project's `defaultBranch` (FEAT-008). Forwarded to the executor as `intake.codeSource.workBranch` on start. |

### WorkItemDto

Extends `WorkItemSummaryDto` with `executorState: object` (opaque) and `signals: CheckpointSignalDto[]` (last N, default 20). Carries the same `workBranch` field.

### CheckpointSignalDto

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| id | uuid | No | |
| checkpointKey | string | No | |
| outcome | string | No | |
| signaledBy | `{ id, displayName }` | No | |
| signaledAt | iso-8601 | No | |
| payload | object | Yes | Contract-shaped |

### AuditEntryDto

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| id | uuid | No | |
| occurredAt | iso-8601 | No | |
| actingMember | `{ id, displayName }` | Yes | |
| project | `{ id, slug }` | Yes | |
| targetType | string | No | |
| targetId | uuid | Yes | |
| action | string | No | |
| outcome | enum | No | `Granted`, `Denied`, `Failed` |
| reason | string | Yes | |

### PendingActionDto

See `GET /api/notifications/pending` response.

### ProblemDetailsDto

Standard RFC 7807 fields (`type`, `title`, `status`, `detail`, `instance`) plus `correlationId` and `errors` (validation-style map).

## Endpoint Summary

| Method | Path | Module | Auth | Description |
|--------|------|--------|------|-------------|
| POST | /api/auth/login | Identity | Public | Issue access token + refresh cookie |
| POST | /api/auth/refresh | Identity | Cookie | Rotate refresh, return new access token |
| POST | /api/auth/logout | Identity | Authenticated | Revoke refresh, clear cookie |
| GET | /api/auth/me | Identity | Authenticated | Current member + memberships |
| GET | /api/teams | Workspace | Authenticated | List teams |
| POST | /api/teams | Workspace | System:operator | Create team |
| GET | /api/teams/{id} | Workspace | Authenticated | Read team |
| PATCH | /api/teams/{id} | Workspace | System:operator | Update team |
| DELETE | /api/teams/{id} | Workspace | System:operator | Soft-delete team |
| GET | /api/members | Workspace | System:operator | List members |
| POST | /api/members | Workspace | System:operator | Invite member |
| GET | /api/members/{id} | Workspace | Authenticated | Read member |
| PATCH | /api/members/{id} | Workspace | System:operator | Update member |
| DELETE | /api/members/{id} | Workspace | System:operator | Soft-delete member |
| GET | /api/projects | Workspace | Authenticated | List projects in scope |
| POST | /api/projects | Workspace | System:operator | Create project |
| GET | /api/projects/{id} | Workspace | Project:any | Read project |
| PATCH | /api/projects/{id} | Workspace | System:operator | Update project |
| DELETE | /api/projects/{id} | Workspace | System:operator | Soft-delete project |
| GET | /api/projects/{id}/memberships | Workspace | Project:any | List memberships |
| POST | /api/projects/{id}/memberships | Workspace | System:operator | Add membership |
| PATCH | /api/projects/{id}/memberships/{mid} | Workspace | System:operator | Update roles |
| DELETE | /api/projects/{id}/memberships/{mid} | Workspace | System:operator | Soft-delete membership |
| GET | /api/roles | Workspace | Authenticated | List role definitions |
| GET | /api/admin/executors | ExecutorRegistry | System:operator | List executors |
| POST | /api/admin/executors | ExecutorRegistry | System:operator | Register executor |
| GET | /api/admin/executors/{id} | ExecutorRegistry | System:operator | Read executor |
| PATCH | /api/admin/executors/{id} | ExecutorRegistry | System:operator | Update executor |
| DELETE | /api/admin/executors/{id} | ExecutorRegistry | System:operator | Soft-delete |
| POST | /api/admin/executors/{id}/checkpoint-contracts | ExecutorRegistry | System:operator | Append/replace contracts |
| GET | /api/admin/executor-bindings | ExecutorRegistry | System:operator | List bindings |
| POST | /api/admin/executor-bindings | ExecutorRegistry | System:operator | Bind project type → executor |
| DELETE | /api/admin/executor-bindings/{id} | ExecutorRegistry | System:operator | Unbind |
| GET | /api/projects/{id}/work-items | WorkItems | Project:any | List work items |
| POST | /api/projects/{id}/work-items | WorkItems | Project:<startRole> | Start a new work item |
| PATCH | /api/projects/{id}/work-items/{wid} | WorkItems | System:operator | Update work item (v1: `workBranch` only) |
| GET | /api/projects/{id}/work-items/{wid} | WorkItems | Project:any | Read work item |
| GET | /api/projects/{id}/work-items/{wid}/checkpoints/{key} | WorkItems | Project:any | Read checkpoint |
| POST | /api/projects/{id}/work-items/{wid}/checkpoints/{key}/signal | WorkItems | Project:<contractRole> | Signal a checkpoint |
| GET | /api/projects/{id}/work-items/{wid}/signals | WorkItems | Project:any | Signal history |
| GET | /api/projects/{id}/work-items/{wid}/stream | WorkItems | Project:any (SSE) | Live trace stream |
| POST | /api/projects/{id}/work-items/{wid}/cancel | WorkItems | Project:<cancelRole> | Cancel work item |
| GET | /api/projects/{id}/audit | Audit | Project:any | Project audit log |
| GET | /api/admin/audit | Audit | System:operator | Cross-project audit log |
| GET | /api/notifications/pending | Notifications | Authenticated | Pending actions for caller |
| GET | /api/notifications/stream | Notifications | Authenticated (SSE) | Live pending-action stream |
| GET | /health | Operations | Public | Liveness |

## AI Task Generation Notes

- **Controller structure**: Each module section above maps to a controller (or a small group of controllers) inside the named module project. Controllers are thin.
- **DTO generation**: Request and response shapes map to DTO classes in `<Module>/DTOs/`. Never expose EF entities directly.
- **Status codes**: Every endpoint that wraps an executor must handle 403 (authorization), 404 (not found), 409 (conflict, e.g. checkpoint already resolved), and 502 (executor failure) at minimum.
- **Auth requirements**: The `Roles` row is binding — implement the authorization check **before** any business logic. Tests must cover both grant and deny paths.
- **Envelope discipline**: Every success response uses `{ "data": ..., "meta"?: ... }`. Errors use RFC 7807 — never wrap errors in the success envelope.
- **Pagination**: Every list endpoint supports `page`, `pageSize`, `sortBy`, `sortDir`. List endpoints must populate `meta.totalCount`.
- **Streaming endpoints are pass-through.** No buffering, no transformation. Authorize at connection time only.
- **Audit on every action**: Every mutation endpoint and every authorization decision writes an `AuditEntry`.

## Changelog

- **2026-05-15** — Initial API specification. Defines the façade surface (auth, workspace CRUD, executor registry, work-items + checkpoints + streams, audit, notifications), the `{ data, meta }` envelope, RFC 7807 errors, JWT auth, project-scoped authorization rules, and the SSE pass-through pattern.
- **2026-05-17 (FEAT-008 / T-057)** — `ProjectDto`, `CreateProjectRequest`, `UpdateProjectRequest` gained optional `repo` (max 140) and `defaultBranch` (max 200). Boundary validation parity with the upstream orchestrator's `intake.codeSource` schema; rejected values produce `400` with no DB write and a `Denied` audit entry. Update audit captures before/after for both fields when they change.
- **2026-05-17 (FEAT-008 / T-058)** — `StartWorkItemRequest`, `WorkItemDto`, `WorkItemSummaryDto` gained optional `workBranch` (max 200). New `PATCH /api/projects/{pid}/work-items/{wid}` endpoint (operator-only) scoped to `workBranch` updates in v1; `null` = leave unchanged, `""` = clear the override, otherwise validate + persist. Branch edits do **not** re-forward to the executor — the value is captured at start time only. `workitem:update` audit details carry before/after.
