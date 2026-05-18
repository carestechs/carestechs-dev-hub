# Architecture

## System Summary

DevHub is a single-deployable **modular monolith** that sits in front of one or more headless lifecycle executors and serves as the only client end users ever touch. It owns workspace primitives (projects, teams, members, roles, memberships), authorization, audit, notifications, and the executor registry; it forwards lifecycle commands and streams to the right executor by project type. The backend is .NET (ASP.NET Core + EF Core + PostgreSQL); the frontend is an Angular SPA; the whole system ships as a small set of Docker images orchestrated by Docker Compose.

### Key Architectural Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Backend topology | Modular monolith (one deployable, many `DevHub.Modules.*` projects) | Hard module boundaries without distributed-system complexity; matches single-org v1 scope. |
| Module isolation | `DbContext` per module; cross-module references by **ID + `DevHub.Contracts/` interface** only | Keeps org context out of executors and out of sibling modules; lets us extract a module later if needed. |
| API host | Thin host (`DevHub.Api`) as composition root only | All controllers/services live inside modules. The host wires DI, middleware, and the request pipeline. |
| Frontend | Angular 20+ SPA, standalone components, Signals, Tailwind utility-only | Front-door is a single app for every project and every lifecycle. Tailwind matches the modern-minimal design system. |
| Database | PostgreSQL via EF Core, snake_case naming, `timestamptz`, UUID PKs | One relational store for all org context; consistent identity strategy end-to-end. |
| Auth | JWT Bearer — short-lived access + rotated refresh | Single identity across every project; no per-executor credentials in the browser. |
| Authorization | End-to-end, pessimistic, at DevHub boundary | Every façade endpoint resolves `(member, role, project, target)` and denies by default before any forward. |
| Live state | Pass-through streaming (chunked HTTP / SSE) | "Adding intelligence in the middle of a stream is forbidden" (Stakeholder Definition rule 4). |
| Errors | RFC 7807 Problem Details everywhere | Uniform error contract across modules and across executor wrappers. |
| Packaging | Docker multi-stage builds (`dotnet/aspnet` for API, `nginx` for SPA) | One image per process, env-agnostic, promotable across stages. |
| Local infra | `docker-compose.yml` for PostgreSQL only; dotnet+ng run on host for hot reload | Fast feedback loop without a Docker round-trip on every change. |
| Prod composition | `docker-compose.prod.yml` for API + frontend on a shared infra network | Matches the carestechs-software-architecture profile; trivial to promote. |
| Executor coupling | **Registry + project-type binding** — no executor knows about org primitives | A second executor of a known shape comes online by configuration only. |

## Technology Stack

| Layer | Technology | Purpose |
|-------|------------|---------|
| **Frontend** | Angular 20+, TypeScript, Tailwind CSS 4+, Angular Signals, RxJS (HTTP/SSE only) | SPA front door, lifecycle-aware screens, design system per `modern-minimal` profile |
| **Backend** | .NET 10+, ASP.NET Core, EF Core, FluentValidation | Modular monolith host + feature modules, façade endpoints, authorization |
| **Data** | PostgreSQL 16+ | Org context, work-item index, audit log, notifications |
| **Infrastructure** | Docker 24+, Docker Compose v2+, nginx | Multi-stage images, local + prod orchestration, SPA serving and `/api/` reverse proxy |
| **Auth** | ASP.NET Core JWT Bearer; Identity module issues access + refresh tokens | Single identity across every project; federation deferred (see Stakeholder Open Questions) |

## Component Architecture

```
┌────────────────────────────┐
│ Angular SPA (client/)      │   nginx serves built SPA, proxies /api/ to API
│  · features/workspace      │
│  · features/projects       │
│  · features/work-items     │
│  · features/lifecycle/*    │   lifecycle-aware screens (per-executor shape)
│  · features/admin          │
└─────────────┬──────────────┘
              │  HTTPS, JWT Bearer
              ▼
┌────────────────────────────────────────────────────────────────────┐
│ DevHub.Api  (thin host — composition root only)                 │
│  · Program.cs: DI, middleware, JWT validation, exception handler,  │
│    AddWorkspaceModule(), AddIdentityModule(),                      │
│    AddExecutorRegistryModule(), AddWorkItemsModule(),              │
│    AddAuditModule(), AddNotificationsModule()                      │
└─────────────┬──────────────────────────────────────────────────────┘
              │  in-process calls via DevHub.Contracts interfaces
   ┌──────────┼───────────────┬───────────────┬──────────────────┐
   ▼          ▼               ▼               ▼                  ▼
┌─────────┐ ┌──────────┐ ┌─────────────────┐ ┌──────────┐ ┌────────────┐
│Workspace│ │ Identity │ │ ExecutorRegistry│ │WorkItems │ │   Audit    │
│         │ │          │ │                 │ │ (façade) │ │ (append-   │
│projects │ │JWT issue │ │executors,       │ │start /   │ │  only log) │
│teams    │ │current   │ │project-type     │ │checkpoint│ │            │
│members  │ │member    │ │bindings,        │ │/ fetch / │ │            │
│roles    │ │          │ │checkpoint       │ │ stream   │ │            │
│member-  │ │          │ │contracts        │ │ proxy    │ │            │
│ ships   │ │          │ │                 │ │          │ │            │
└────┬────┘ └────┬─────┘ └────────┬────────┘ └────┬─────┘ └─────┬──────┘
     │           │                │               │              │
     └───────────┴────────┬───────┴───────────────┴──────────────┘
                          ▼
                  ┌────────────────┐
                  │   PostgreSQL   │   one DB instance, one schema per module
                  └────────────────┘

                  ┌────────────────────────┐
                  │ Notifications module   │   pending-action signal surface
                  └────────────────────────┘

   WorkItems façade ──► Lifecycle Executor A (e.g. feature-delivery)
                  └──► Lifecycle Executor B (future, configured at runtime)
```

### Component Descriptions

**Angular SPA (`client/`)**
- **Purpose:** The single front door for every end user. Renders project lists, work-item lists, lifecycle-aware review screens, the operator dashboard, and admin pages for workspace primitives.
- **Responsibilities:** Authentication flow, identity persistence (in-memory access token + httpOnly refresh cookie), routing per `(project, role)` permissions, rendering pass-through streams (SSE) without buffering.
- **Key Dependencies:** `DevHub.Api` for every action; nginx for static serving and `/api/` reverse proxy.

**`DevHub.Api` (thin host)**
- **Purpose:** Composition root. Wires modules, middleware, JWT validation, the global RFC 7807 exception handler, and the request pipeline.
- **Responsibilities:** Only DI registration and pipeline composition. **No controllers, no services, no business logic live here.**
- **Key Dependencies:** Every `DevHub.Modules.*` project, `DevHub.Contracts`.

**`DevHub.Modules.Workspace`**
- **Purpose:** Owner of org primitives — Projects, Teams, Members, Roles, and ProjectMemberships.
- **Responsibilities:** CRUD for all workspace entities; project-membership lookups; the central `IProjectAuthorizationService` exposed via `DevHub.Contracts`.
- **Key Dependencies:** PostgreSQL (`WorkspaceDbContext`); `DevHub.Modules.Audit` (records every mutation).

**`DevHub.Modules.Identity`**
- **Purpose:** Authentication and current-member resolution.
- **Responsibilities:** Issues short-lived access tokens and rotated refresh tokens; resolves `ICurrentMember` for every request; password / credential storage. (Identity-provider posture is an open question; this module is the seam where federation can land later.)
- **Key Dependencies:** PostgreSQL (`IdentityDbContext`); `DevHub.Modules.Workspace` (via `IProjectMembershipQuery` in `DevHub.Contracts`).

**`DevHub.Modules.ExecutorRegistry`**
- **Purpose:** The "configuration-only second executor" surface.
- **Responsibilities:** CRUD for executor registrations (id, base URL, credentials reference, declared checkpoint contracts); bindings from project type → executor; exposes `IExecutorRouter` that resolves a project to its executor.
- **Key Dependencies:** PostgreSQL (`ExecutorRegistryDbContext`); credentials store (out of scope for v1 — env-var injected). Operator-only access.

**`DevHub.Modules.WorkItems` (the façade)**
- **Purpose:** The DevHub-mediated entry point for every action that touches a lifecycle.
- **Responsibilities:** Project-scoped work-item index (id, project, type, current state snapshot, executor correlation marker); endpoints to start work, send a checkpoint signal, fetch state, and **stream** live progress; performs the `(member, role, project, target)` authorization check before every forward; writes an audit entry for every action and outcome.
- **Key Dependencies:** `IExecutorRouter` (from `ExecutorRegistry`), `IProjectAuthorizationService` (from `Workspace`), `IAuditWriter` (from `Audit`), the executor's HTTP API via `HttpClient` + `IHttpClientFactory`.

**`DevHub.Modules.Audit`**
- **Purpose:** Append-only record of every DevHub-mediated action.
- **Responsibilities:** `IAuditWriter` writes `AuditEntry` rows inside the caller's transaction; query endpoints for the operator dashboard (filtered by project, member, action, outcome).
- **Key Dependencies:** PostgreSQL (`AuditDbContext`).

**`DevHub.Modules.Notifications`**
- **Purpose:** "A member discovers pending action without polling."
- **Responsibilities:** Maintains pending-action signals scoped to `(member, project, work-item, checkpoint)`. v1 mechanism is in-app (WebSocket or SSE push from the API); the schema permits future channels (email, webhook). Channel selection is an open question.
- **Key Dependencies:** `WorkItems` events; PostgreSQL (`NotificationsDbContext`); SignalR (or SSE) hub in `DevHub.Api`.

**Lifecycle Executors (external)**
- **Purpose:** Single-concern engines that advance one work item through one lifecycle.
- **Responsibilities:** Owned outside this codebase. Expose start, checkpoint, fetch-state, and live-stream endpoints. Are completely unaware of projects, teams, members, or roles.
- **Key Dependencies:** None on DevHub. DevHub holds their credentials; they hold nothing from DevHub beyond the correlation marker passed on every command.

### Executor protocols (FEAT-010)

DevHub talks to executors through `IExecutorHttpClient`. Two implementations ship today:

- **`ExecutorHttpClient`** — speaks DevHub's native protocol (`POST /work-items`, `POST /work-items/{marker}/checkpoints/{key}/signal`, etc.). Used by the FakeExecutor in tests and any executor that adopts DevHub's wire shape.
- **`OrchestratorExecutorClient`** — speaks the carestechs-agent-orchestrator's `/api/v1/runs` API. Translates DevHub's calls to the orchestrator's routes; maps `RunStatus` → `CurrentStatus`; derives `currentCheckpointKey` + `currentTaskId` from trace records; converts NDJSON trace to SSE inline.

Selection happens per executor via `ExecutorRegistration.Protocol` (`"devhub"` or `"orchestrator"`; default `"devhub"`). `IExecutorClientFactory.Resolve(descriptor)` returns the right implementation. The `WorkItem` row carries both `ExecutorCorrelationMarker` (DevHub's id) and `ExecutorRunId` (the orchestrator's run id, populated after Start succeeds).

The decision to live in-process — rather than as a sibling adapter service — is recorded in the FEAT-010 brief (§11).

## Data Flow

**Member-initiated checkpoint action (the canonical path):**

1. Member opens a project, opens a work item, clicks "Approve" on a checkpoint waiting on their role.
2. Angular sends `POST /api/projects/{projectId}/work-items/{workItemId}/checkpoints/{checkpointId}/signal` with `{ outcome, payload }`.
3. `DevHub.Api` validates the JWT, resolves `ICurrentMember`.
4. `WorkItemsController` calls `IProjectAuthorizationService.AuthorizeAsync(member, projectId, workItemId, action: "checkpoint:signal", role-required: <from checkpoint contract>)`. **Denied requests never reach the executor** — they return 403 and write a denied-action audit entry.
5. On grant, `WorkItemsService` resolves the executor via `IExecutorRouter`, posts the signal with the work-item's correlation marker, writes an audit entry inside the same transaction, and returns the updated state DTO.
6. `DevHub.Modules.Notifications` removes the signal-pending entry for that member/checkpoint (and emits new ones for the next checkpoint, if any).
7. Angular subscribes to the work-item's live stream (SSE) and renders the trace as it advances.

**Streamed live state (the hot path):**

1. Angular opens `GET /api/projects/{projectId}/work-items/{workItemId}/stream` (SSE).
2. `DevHub.Api` authorizes once at connection time, then proxies bytes from the executor's stream to the client **without buffering, batching, or transformation**.
3. On client disconnect, the upstream connection is closed; no replay state is held in DevHub.

**Operator registers a second executor:**

1. Operator (admin role) calls `POST /api/admin/executors` with `{ id, baseUrl, credentialsRef, checkpointContracts[] }` and `POST /api/admin/executor-bindings` with `{ projectType, executorId }`.
2. No code change required; `IExecutorRouter` resolves the new project type on the next request.

## Integration Points

| Service | Purpose | Auth Method | Failure Strategy |
|---------|---------|-------------|------------------|
| Lifecycle Executor A (feature-delivery) | Advances feature work items through their lifecycle | Bearer or API key (per executor registration), held only in DevHub | Surface as 502 with problem-detail; audit log records the failure; client retries via the user re-clicking — no background queue in v1 |
| Lifecycle Executor B+ (future, e.g. incident response) | Same as A, different lifecycle | Same | Same |
| Identity provider (future) | External SSO | Federation deferred — open question in stakeholder def | N/A in v1 |

v1 has **no** other planned external integrations. Notifications channels beyond in-app (email, webhook) are deferred.

## Security Architecture

- **Authentication.** ASP.NET Core JWT Bearer. The Identity module issues short-lived access tokens (in-memory in Angular) and rotated refresh tokens (httpOnly cookie). Every request reaches a controller with `ICurrentMember` resolved or is rejected at the middleware layer.
- **Authorization.** End-to-end and pessimistic. Every façade endpoint resolves `(member, role, project, target)` via `IProjectAuthorizationService.AuthorizeAsync` and is denied by default. The check is the **first non-validation line** of every controller action that wraps an executor call. Denied actions are audited and never forwarded.
- **Executor credentials.** Held only in DevHub process, injected via environment variables and referenced by `credentialsRef` in the executor registry. Never exposed to the browser, never logged, never returned in any API response.
- **Data protection.** PostgreSQL connection over TLS in production. Refresh tokens are hashed at rest. Audit entries are append-only (no UPDATE, no DELETE) and retained for the operator dashboard.
- **API security.** RFC 7807 problem details for every error path. Input validation via FluentValidation at the controller boundary. CORS restricted to the SPA origin. Rate limiting on auth endpoints (open question on values). Streaming endpoints inherit standard auth + a per-connection authorization check at open time.
- **Audit.** Every DevHub-mediated action (grant or deny) writes an `AuditEntry`. The operator dashboard surfaces these. Deletes of org primitives are soft; audit is hard.

## AI Task Generation Notes

> These notes help AI assistants generate technically correct tasks.

- **Honor the front-door rule.** Any new end-user-facing surface enters through `DevHub.Api`. Never propose a direct browser-to-executor call or a per-executor admin tool for end users.
- **Org primitives belong only in the Workspace module.** Do not propose adding projects, teams, members, or roles to any lifecycle executor.
- **Authorization is mandatory and first.** Every new façade endpoint MUST declare the `(role, project, target)` check it performs and verify it before any forward. PRs without an explicit deny-path test are review blockers.
- **Streaming is pass-through.** Never buffer, batch, or transform live traces. Adding "intelligence in the middle of a stream" is forbidden.
- **Module boundaries are hard.** Cross-module communication goes through `DevHub.Contracts/` interfaces and references entities by ID. Do not add cross-module navigation properties or shared DbContexts.
- **Executor-agnostic by construction.** If you find yourself adding a code path that hard-codes a single executor's URL or DTO, route it through `IExecutorRouter` and the registry's checkpoint contracts instead.
- **No cross-project state.** Every work item, every audit entry, every notification is scoped to exactly one project.
- **Modern-minimal design system applies to every new screen.** See `docs/ui-specification.md` Design System.

## Changelog

- **2026-05-15** — Initial architecture draft, compiled from the `dotnet-angular-modular-monolith-docker-compose` profile and DevHub stakeholder definition. Defines module list (Workspace, Identity, ExecutorRegistry, WorkItems, Audit, Notifications), data flow for member checkpoint actions and streaming, and the security/authorization model.
- **2026-05-17 (FEAT-010)** — Added `OrchestratorExecutorClient` as a second `IExecutorHttpClient` implementation; selection via `ExecutorRegistration.Protocol`. The original "Adapter service" framing was discarded in favor of an in-process class; brief §11 records the reasoning.
