# CLAUDE.md

> This file provides guidance to Claude Code (or any AI assistant) when working with this codebase.

## Pre-Work Checklist

Before generating specs, tasks, mockups, or implementation plans, you MUST follow these steps:

1. **Identify the task type** using the routing table in the "AI-Assisted Development Framework" section below. **If working on a specific task (T-XXX), check its Workflow field** and follow the Workflow Enforcement rules before starting implementation.
2. **Read the required files** listed in the routing table for your task type — read them directly, do not ask the user to paste them.
3. **Read the prompt template** from `.ai-framework/prompts/` — this defines the required sections, structure, and quality criteria for the deliverable.
4. **Derive structure from the prompt template, NOT from existing output files.** Specs, tasks, and plans are *outputs* — they may reflect an older version of the framework. The prompt templates in `.ai-framework/prompts/` are the authoritative source for format and structure.

---

## Project Overview

Carestechs Dev Hub (working name: **DevHub**) is the multi-project, multi-team workspace that sits *above* one or more headless lifecycle executors and serves as the single front door humans use to start, observe, approve, and complete work. Org context (projects, teams, members, roles, assignments, authorization) lives here; lifecycle mechanism lives downstream in the executors. See `docs/stakeholder-definition.md` for full scope.

**Tech Stack:** .NET 10+ (ASP.NET Core, EF Core, PostgreSQL) backend as a **modular monolith**; Angular 20+ SPA with Tailwind CSS 4+ frontend; Docker Compose for orchestration. Profile: `dotnet-angular-modular-monolith-docker-compose` (see `docs/ARCHITECTURE.md`).
**Repo Type:** Monorepo — single solution with backend modules under `src/`, Angular client under `client/`, single deployable.

---

## Quick Reference

### Common Commands

```bash
# Backing services (PostgreSQL)
docker compose up -d

# Backend (hot-reload)
dotnet run --project src/DevHub.Api

# Backend migrations (per module)
dotnet ef database update --project src/DevHub.Modules.Workspace
dotnet ef database update --project src/DevHub.Modules.Identity
dotnet ef database update --project src/DevHub.Modules.ExecutorRegistry
dotnet ef database update --project src/DevHub.Modules.WorkItems
dotnet ef database update --project src/DevHub.Modules.Audit
dotnet ef database update --project src/DevHub.Modules.Notifications

# Frontend
cd client && ng serve --proxy-config proxy.conf.json

# Tests
dotnet test
cd client && ng test

# Production
docker compose -f docker-compose.prod.yml build
docker compose -f docker-compose.prod.yml up -d
```

### Key Directories

```
src/
├── DevHub.Api/                          # Thin API host (composition root). No controllers, services, or business logic here.
├── DevHub.Contracts/                    # Shared interfaces and DTOs for cross-module communication.
├── DevHub.Modules.Workspace/            # Projects, Teams, Members, Roles, ProjectMemberships.
├── DevHub.Modules.Identity/             # Authentication, JWT issuance, current-member resolution.
├── DevHub.Modules.ExecutorRegistry/     # Lifecycle executor registrations, project-type bindings, checkpoint contracts.
├── DevHub.Modules.WorkItems/            # Project-scoped work items, façade to executors (start, checkpoint, fetch state, stream).
├── DevHub.Modules.Audit/                # Append-only audit entries for every DevHub-mediated action.
└── DevHub.Modules.Notifications/        # Pending-action signal surface.

client/
├── src/app/core/                           # Singletons: auth, http interceptors, identity service.
├── src/app/shared/                         # Reusable standalone components, pipes, directives.
└── src/app/features/                       # Route-based features: workspace, projects, work-items, lifecycle-screens, admin.
```

---

## Code Style & Conventions

- **Modular monolith with hard boundaries.** Each `DevHub.Modules.*` project owns its own `DbContext`, controllers, services, entities, and DTOs. Cross-module communication is **by ID + shared interface in `DevHub.Contracts/` only** — no cross-module navigation properties, no shared DbContext.
- **Thin API host.** `DevHub.Api/Program.cs` is composition root only. Controllers live inside modules. No business logic in `DevHub.Api`.
- **Service-layer logic.** Controllers are thin: parse → call service → return DTO. Business logic lives in service classes with `Async` suffix on async methods.
- **DTO at boundary.** EF entities never leave the service layer. Mapping happens in services.
- **Async all the way.** All I/O is `async/await`. No `.Result` / `.Wait()`.
- **Angular standalone components.** No `NgModules`. Templates in separate `.html` files via `templateUrl`. Tailwind utility classes only — no component CSS files.
- **Angular Signals for reactive state.** RxJS only for HTTP and async streams (e.g., SSE for live trace).
- **Auth at the boundary.** Every controller action that wraps an executor call resolves `(member, role, project, target)` and calls the project authorization service **before** any forward. Denied by default.
- **Streaming is hot path.** Endpoints that wrap executor streams (SSE/WebSocket) pass through — no buffering, batching, or transformation in DevHub.

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| C# files | PascalCase | `ProjectMembershipService.cs` |
| C# types/interfaces | PascalCase (interfaces `I` prefix) | `IProjectAuthorizationService` |
| C# methods | PascalCase + `Async` suffix on async | `AuthorizeAsync` |
| Angular component files | kebab-case + role suffix | `project-list.component.ts` |
| Angular utility files | kebab-case | `format-date.ts` |
| TypeScript functions | camelCase | `getProjectById` |
| TypeScript types/interfaces | PascalCase | `ProjectMembership` |
| Constants | UPPER_SNAKE | `MAX_STREAM_RETRY` |
| Database tables/columns | snake_case (via EF naming convention) | `project_membership`, `created_at` |
| JSON properties | camelCase (System.Text.Json default) | `projectId`, `createdAt` |
| URLs | kebab-case, plural resources | `/api/projects/{id}/work-items` |

---

## Patterns & Anti-Patterns

### Patterns to Follow

- **Authorization-first endpoints.** Every façade endpoint that wraps an executor call MUST declare its `(role, project, target)` check and verify it before the forward. The check is the first non-validation line of the controller action.
- **REST envelope.** All responses are wrapped in `{ "data": ..., "meta": ... }`. List endpoints use offset pagination (`page`, `pageSize`, `sortBy`, `sortDir`) in the envelope.
- **RFC 7807 problem details.** All errors return `application/problem+json` via the global exception handler. No custom error envelope.
- **JWT Bearer auth.** Short-lived access token + rotated refresh token. Angular stores access token in memory; refresh in httpOnly cookie.
- **UUIDs everywhere.** All primary keys are UUIDs generated in C#. Database stores `uuid`, JSON serializes as strings.
- **`timestamptz` always.** Backend uses `DateTimeOffset`; database uses `timestamptz`; Angular converts to local time for display.
- **Soft deletes on workspace primitives.** Projects, teams, members, memberships use a nullable `deleted_at` column. Audit entries are append-only and never deleted.
- **Audit on the way out.** Every DevHub-mediated mutation writes an `AuditEntry` (member, project, target, action, outcome) inside the same transaction as the mutation.
- **Live state passes through.** Streaming endpoints proxy the executor stream (chunked HTTP / SSE) without buffering. Polling endpoints add no caching.
- **Executor-agnostic façade.** Adding a second registered executor of a known shape is configuration only — bind it in the `ExecutorRegistry` and route by project type. No new code in `DevHub.Api` for the routine case.

### Design Patterns to Follow (UI — Modern Minimal)

- **Cards are elevated, not bordered.** Default `shadow-sm`, `hover:shadow-md` on clickable cards, no border. Hover lift: `hover:-translate-y-0.5 transition-all duration-200`.
- **Generous whitespace.** Card padding `p-6`, section gaps `gap-8`, page padding `py-8`.
- **Reading-focused widths.** Detail/review pages use `max-w-5xl`. Dashboards may use `max-w-7xl`.
- **Buttons use `rounded-lg`** (softer than the default `rounded-md`).
- **Typography:** Poppins for headings, Inter for body. Body line-height 1.6 for readability.
- **Primary color is sky-500** (`#0EA5E9`). Success emerald-500, warning amber-500, error red-500.
- **Mobile-first responsive.** Base styles assume mobile; layer up with `sm:`, `md:`, `lg:`, `xl:` breakpoints.
- **Focus-visible rings on every interactive element.** WCAG AA contrast on all text/background pairs.

### Anti-Patterns to Avoid

- **Do not let any end-user-facing flow bypass DevHub.** No direct-to-executor links, no client-side executor credentials. If a user can reach it from a screen, it routes through DevHub façade.
- **Do not add org primitives (projects, teams, members, roles) to any lifecycle executor.** Org context lives only in DevHub. If an executor "needs to know" who someone is, surface that as a feature on that executor's backlog, not as a private back-channel.
- **Do not put authorization checks in the executor layer.** Auth happens at DevHub boundary, before the forward. The executor is assumed to be unauthenticated relative to end users.
- **Do not buffer, batch, or transform streamed traces in the middle of a stream.** Live state must feel native.
- **Do not introduce cross-module navigation properties or shared DbContexts.** Reference by ID; expose via `DevHub.Contracts/` interfaces.
- **Do not introduce cross-project work items or shared lifecycle state.** Every work item belongs to exactly one project.
- **Do not put business logic in controllers or in `Program.cs`.** Services own logic; controllers parse and respond; `Program.cs` composes.
- **Do not expose EF entities via the API.** Always map to DTOs in the service layer.
- **Do not use inline Angular templates or component CSS files.** Templates go in `.html` files; styling is Tailwind utility classes only.
- **Do not use `console.log` / `Console.WriteLine`.** Use structured logging (ILogger in .NET, the shared logger service in Angular).

---

## Error Handling

- **Domain exceptions** (`NotFoundException`, `ForbiddenException`, `ValidationException`, `ConflictException`) thrown from services are translated by a global exception handler into RFC 7807 problem details.
- **Authorization failures** (`(member, role, project, target)` mismatch) return `403` with a problem detail of `type: /probs/forbidden` and never reach the executor.
- **Executor failures** are translated to `502 Bad Gateway` with a problem detail that includes the executor identifier and a correlation id. The executor's own error body is propagated under `details` so operators can debug; user-facing screens render the human title only.
- **All errors and authorization denials are written to the audit log** with the failed check and outcome. Never swallow silently.
- **Angular HTTP interceptor** centralizes problem-detail parsing; component code consumes a normalized `AppError` type.

---

## Testing Conventions

- **Test location:** `tests/DevHub.Modules.<Module>.Tests/` per module; Angular tests co-located as `*.spec.ts`.
- **Naming:** `*Tests.cs` for .NET (xUnit), `*.spec.ts` for Angular (Jasmine/Karma; Playwright for e2e).
- **Framework:** xUnit + FluentAssertions for backend; Testcontainers for integration tests against real PostgreSQL; Angular Testing Library / Jasmine for components; Playwright for end-to-end.
- **Priority:**
  - Unit tests for service-layer business logic and authorization checks (every authorize call is tested for grant AND deny paths).
  - Integration tests for every façade endpoint that wraps an executor (use a fake executor double).
  - One end-to-end happy path per lifecycle-aware screen.
- **Authorization is a tested concern, not a reviewed concern.** Every new façade endpoint MUST ship with at least one deny-path test.

---

## Git Conventions

- **Branch naming:** `feat/<short-desc>`, `fix/<short-desc>`, `chore/<short-desc>`, `docs/<short-desc>`.
- **Commit style:** Conventional commits — `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`.
- **PR requirements:** CI green (build, tests, lint), one review, doc updates per the maintenance table when applicable, no new façade endpoint without an authorization deny-path test.

---

## AI-Assisted Development Framework

This project includes a bundled AI framework (`.ai-framework/`) with prompt templates, context assembly guides, and documentation maintenance rules.

**If you are an AI agent (e.g., Claude Code):** Read the files listed in the routing table below directly — do not ask the user to paste them. Read the prompt template for your task type to determine the output format. For manual/chat workflows, see `.ai-framework/guides/context-compilation.md` for XML assembly instructions.

### Task Generation Routing

When asked to generate tasks, identify the task type, read the required files, then read the prompt template for output format.

| Task Type | Prompt Template | Files to Read |
|-----------|----------------|---------------|
| New feature | `.ai-framework/prompts/feature-tasks.md` | `docs/work-items/FEAT-*.md` (target feature), `docs/stakeholder-definition.md`, `CLAUDE.md`, `docs/data-model.md`, `docs/api-spec.md`, `docs/ui-specification.md` |
| Bug fix | `.ai-framework/prompts/bugfix-tasks.md` | `docs/work-items/BUG-*.md` (target bug), `CLAUDE.md`, `docs/ARCHITECTURE.md` |
| Refactoring | `.ai-framework/prompts/refactor-tasks.md` | `docs/work-items/IMP-*.md` (target improvement), `CLAUDE.md`, `docs/ARCHITECTURE.md` |
| Spec generation | `.ai-framework/prompts/spec-generation.md` | `docs/stakeholder-definition.md`, `CLAUDE.md`, `docs/ARCHITECTURE.md` |
| UI spec generation | `.ai-framework/prompts/ui-spec-generation.md` | `docs/stakeholder-definition.md`, `CLAUDE.md`, `docs/ARCHITECTURE.md`, `docs/api-spec.md` |
| UI mockup | `.ai-framework/prompts/mockup-generation.md` | `docs/ui-specification.md` (target screen + Design System), `CLAUDE.md` |
| ADR compilation | `.ai-framework/prompts/compile-adrs.md` | ADR files (from shared ADR repo), `.ai-framework/templates/` |
| DDR compilation | `.ai-framework/prompts/compile-ddrs.md` | DDR files (from shared DDR repo), `.ai-framework/templates/` |
| Release transition | `.ai-framework/guides/release-lifecycle.md` | `docs/stakeholder-definition.md`, `CLAUDE.md` |
| Task implementation plan | `.ai-framework/prompts/plan-generation.md` | `CLAUDE.md`, task definition, files listed in task's "Files to Modify/Create" |

**Optional context** (read only when relevant to the specific task):

| Task Type | Optional Files | When to Include |
|-----------|---------------|-----------------|
| New feature | `docs/ARCHITECTURE.md`, `docs/personas/primary-user.md` | Multi-component features, user-facing features |
| Bug fix | `docs/data-model.md`, `docs/api-spec.md`, `docs/ui-specification.md` | Data/API/UI bugs respectively |
| Refactoring | `docs/data-model.md`, `docs/stakeholder-definition.md` | Data refactors, scope questions |
| Spec generation | `docs/personas/primary-user.md` | User-facing entity/endpoint decisions |
| UI mockup | `docs/api-spec.md`, `docs/personas/primary-user.md` | Data-driven screens, content tone |
| Prioritization | `docs/work-items/FEAT-*.md`, `docs/work-items/BUG-*.md`, `docs/work-items/IMP-*.md`, `docs/stakeholder-definition.md`, `docs/personas/` | Comparing and prioritizing work items |

**Work Items** (`docs/work-items/`): Feature Briefs, Bug Reports, and Improvement Proposals are the preferred input for task generation. If no work item document exists for a task, the prompts support inline fallbacks — but structured work items produce higher-quality task breakdowns.

### Workflow Enforcement

Each task definition includes a **Workflow** field. Before starting any task, check its Workflow value and follow the required steps:

| Workflow | Required Steps Before Implementation |
|----------|--------------------------------------|
| `standard` | 1. Generate an implementation plan using `.ai-framework/prompts/plan-generation.md`. Output: `plans/plan-T-XXX-short-title.md`. 2. Implement following the plan. |
| `mockup-first` | 1. Generate an HTML mockup using `.ai-framework/prompts/mockup-generation.md`. Get stakeholder approval. See `.ai-framework/guides/getting-started.md` Step 7.5. 2. Generate an implementation plan using `.ai-framework/prompts/plan-generation.md`. Output: `plans/plan-T-XXX-short-title.md`. 3. Implement following the plan. |
| `investigation-first` | 1. Complete all investigation steps in the task. Document findings (root cause, affected areas). 2. Generate an implementation plan using `.ai-framework/prompts/plan-generation.md`. Output: `plans/plan-T-XXX-short-title.md`. 3. Implement following the plan. |

**If a task has no Workflow field** (legacy tasks), classify it yourself:
- Type is Frontend + adds/changes a screen → treat as `mockup-first`
- Task requires root cause analysis → treat as `investigation-first`
- Otherwise → treat as `standard`

### Development Pipeline

When implementing tasks from a generated task list, follow this sequence for **each task**:

1. **Pick a task** from the task list (respect dependency order).
2. **Check its Workflow field** and complete any prerequisites (see Workflow Enforcement above).
3. **Generate an implementation plan** using `.ai-framework/prompts/plan-generation.md`. Output: `plans/plan-T-XXX-short-title.md`.
4. **Implement** following the steps in the plan.
5. **Verify** the acceptance criteria from the task definition are met.

This sequence applies to every task. The plan file is a developer-facing artifact — it bridges "what to do" (task definition) and "how to do it" (exact code changes).

### Context Assembly Rules

Read files in **Cone of Context** order — broad (strategic) to narrow (tactical):

| Layer | Files | Purpose |
|-------|-------|---------|
| Strategic | `docs/stakeholder-definition.md`, `docs/personas/primary-user.md` | Why? For whom? What's in scope? |
| Architectural | `docs/ARCHITECTURE.md` | What is the system? How is it structured? |
| Specification | `docs/data-model.md`, `docs/api-spec.md` | What are the entities and API contracts? |
| UI | `docs/ui-specification.md` | What do screens look like? What are the components? |
| Work Items | `docs/work-items/FEAT-*.md`, `docs/work-items/BUG-*.md`, `docs/work-items/IMP-*.md` | What specific work to do? Features, bugs, improvements |
| Implementation | `CLAUDE.md` | How do we build things? What are the conventions? |

**For large documents:** Read only the sections relevant to the task (e.g., for a task about labels, read only the Label entity from `data-model.md` and label endpoints from `api-spec.md`). Quality over quantity.

For the full context selection matrix and XML assembly examples, see `.ai-framework/guides/context-compilation.md`.

### Documentation Maintenance Discipline

When code changes happen, check which docs need updating per `.ai-framework/guides/maintenance.md`. Include doc updates in the same PR as the code change.

| Code Change | Document to Update |
|-------------|-------------------|
| New entity or field | `docs/data-model.md` |
| New/changed endpoint or DTO | `docs/api-spec.md` |
| New/changed screen or component | `docs/ui-specification.md` |
| New component or service | `docs/ARCHITECTURE.md` |
| New pattern or convention | `CLAUDE.md` |
| Scope or strategy change | `docs/stakeholder-definition.md` |
| Design token or screen layout change | `mockups/` (affected screens) |
| DDR updated in shared repo | Re-run DDR compilation, update Component Examples + CLAUDE.md Design Patterns |
| Feature tasks completed | `docs/work-items/FEAT-*.md` — update Status to "Completed" |
| Bug resolved | `docs/work-items/BUG-*.md` — update Status to "Resolved" |
| Improvement completed | `docs/work-items/IMP-*.md` — update Status to "Completed" |

**Changelog rule:** Every update to `data-model.md`, `api-spec.md`, `ARCHITECTURE.md`, or `ui-specification.md` must include a changelog entry at the bottom of the document. See `.ai-framework/guides/maintenance.md` for format.

### Framework Reference

For deeper reading on the full workflow and rules:

- `.ai-framework/guides/getting-started.md` — full workflow from docs to task generation
- `.ai-framework/guides/context-compilation.md` — context assembly details and task-type matrix
- `.ai-framework/guides/maintenance.md` — doc update triggers and review checklists
