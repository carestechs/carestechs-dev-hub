# FEAT-002 Task Breakdown — Workspace Primitives

> Generated from `docs/work-items/FEAT-002-workspace-primitives.md` using `.ai-framework/prompts/feature-tasks.md`. 7 tasks across Backend, Testing, and Frontend.

## Scope choices locked in before generation

- **Audit:** ship a minimal Audit module alongside FEAT-002 (`AuditEntry` + `IAuditWriter` + migration); FEAT-006 lands the query endpoints and the operator dashboard.
- **Operator authorization:** workspace-global. An operator with the system `operator` role may act on any project without holding a `ProjectMembership`. Every other role is per-project.
- **Soft delete:** existing `deleted_at` columns from T-005 carry through unchanged.

---

## Backend

### T-022: Audit module — minimal entity + IAuditWriter

**Type:** Backend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-001..T-009 (foundation)

**Description:**
Land `AuditEntry` per the data model under the `audit` schema. Publish `IAuditWriter` from `DevHub.Contracts` so other modules can record `Granted` / `Denied` / `Failed` outcomes from inside their own service-layer transactions, without referencing the Audit module directly.

**Rationale:**
FEAT-002 AC-2 ("denies are audited"), AC-4 (soft delete audited), AC-5 (deny test required). Every mutation in T-024 calls `IAuditWriter.WriteAsync` inside the same transaction as the change.

**Acceptance Criteria:**
- [ ] `dotnet ef database update --project src/DevHub.Modules.Audit` creates `audit.audit_entries` with all columns from data-model.md.
- [ ] `IAuditWriter.WriteAsync(...)` appends one row and never updates or deletes (enforced by the implementation, not by a trigger in v1).
- [ ] `Granted`/`Denied`/`Failed` outcomes round-trip end-to-end.
- [ ] Integration test asserts that an audit row is written inside the *same* DbContext transaction as the mutation that triggered it.

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.Audit/Entities/AuditEntry.cs`, `Enums/AuditOutcome.cs`
- Modify: `src/DevHub.Modules.Audit/AuditDbContext.cs` (DbSet + mappings + indexes)
- Create: `src/DevHub.Modules.Audit/Migrations/*` (real migration, replacing T-009's empty one)
- Create: `src/DevHub.Modules.Audit/Services/AuditWriter.cs` (internal)
- Create: `src/DevHub.Contracts/Audit/IAuditWriter.cs`, `AuditOutcome.cs`, `AuditWriteRequest.cs`
- Modify: `src/DevHub.Modules.Audit/AuditModuleExtensions.cs` (register `IAuditWriter`)
- Create: `tests/DevHub.Modules.Audit.Tests/AuditWriterTests.cs`

**Technical Notes:**
`AuditEntry` is `BaseEntity` (so `CreatedAt`/`UpdatedAt` come for free) but the `OccurredAt` field is the canonical timestamp the caller controls (defaults to `DateTimeOffset.UtcNow`). Indexes on `(project_id, occurred_at desc)`, `(acting_member_id, occurred_at desc)`, `(outcome, occurred_at desc)` per data-model. `IAuditWriter.WriteAsync` calls `AuditDbContext.AuditEntries.Add(...)` and `SaveChangesAsync` only if the caller hasn't supplied an outer transaction; otherwise it just stages the insert.

---

### T-023: Cross-module contracts — authorization + lookups

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-022

**Description:**
Publish the cross-module surfaces every authorized endpoint needs, all under `DevHub.Contracts/`. Two interfaces with one canonical implementation in Workspace:
- `IProjectAuthorizationService.AuthorizeAsync(memberId, projectId, action, requiredRoleKey?)` — returns `AuthorizationOutcome` and writes an audit entry on grant or deny. Operators get a workspace-wide grant.
- `IProjectMembershipQuery.GetMembershipsAsync(memberId)` — returns the member's `(projectId, projectSlug, roles)` tuples; used by Identity to populate `/api/auth/me` and to embed roles in fresh JWTs.

Also extend `IMemberLookup` so consumers can resolve `Member` + status from a single call.

**Rationale:**
Authorization is the spine of every FEAT-002 endpoint. Centralizing the check in one service + auditing it from inside avoids "did this endpoint remember to audit?" drift.

**Acceptance Criteria:**
- [ ] `IProjectAuthorizationService.AuthorizeAsync` returns `Granted` when caller is operator (regardless of memberships) and writes an audit entry.
- [ ] Returns `Denied` (audit'd) when caller is not on the project.
- [ ] Returns `Denied` (audit'd) when caller is on the project but lacks the `requiredRoleKey`.
- [ ] Returns `Granted` when caller is on the project AND `requiredRoleKey` is null (any-role read).
- [ ] `IProjectMembershipQuery.GetMembershipsAsync` returns `[]` for an unprivileged member with no memberships; returns the operator's project-scoped roles correctly otherwise.

**Files to Modify/Create:**
- Create: `src/DevHub.Contracts/Authorization/IProjectAuthorizationService.cs`, `AuthorizationOutcome.cs`, `AuthorizationDeniedException.cs` (subclass of `ForbiddenException`)
- Create: `src/DevHub.Contracts/Authorization/IProjectMembershipQuery.cs`, `MembershipDto.cs`
- Create: `src/DevHub.Modules.Workspace/Services/ProjectAuthorizationService.cs`, `ProjectMembershipQuery.cs`
- Modify: `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs` (register both)
- Modify: `src/DevHub.Contracts/Identity/IMemberLookup.cs` (already takes status — no shape change; just confirm)
- Create: `tests/DevHub.Modules.Workspace.Tests/ProjectAuthorizationServiceTests.cs` (5 cases above + audit assertion)

**Technical Notes:**
`AuthorizationOutcome` is a sealed record `{ bool Granted, string? DeniedReason }`. `ProjectAuthorizationService` writes the audit entry **before** returning so callers can't forget. The deny path: operator presence check → membership lookup → role match. Operator status is "member holds the `operator` system role via any project membership OR a workspace-level role assignment" — for v1, a single seeded operator member is special-cased by having an `operator` role on a virtual `_workspace_` project, OR (simpler) we add a workspace-level `is_operator` flag to `Member` (decide in T-024).

---

### T-024: Workspace services + controllers (Teams, Members, Projects, Memberships, Roles)

**Type:** Backend · **Workflow:** standard · **Complexity:** XL · **Dependencies:** T-023

**Description:**
Land all 20-odd endpoints under `/api/teams`, `/api/members`, `/api/projects`, `/api/projects/{id}/memberships`, `/api/roles` per `docs/api-spec.md`. Each controller action is thin (validate → authorize → call service → return envelope DTO). Every mutation goes through `IProjectAuthorizationService` (for project-scoped writes) or a workspace-level `[Authorize(Roles=...)]` policy (for operator-only writes), and every successful mutation writes a `Granted` audit entry. Every deny writes a `Denied` audit entry.

**Rationale:**
This is the bulk of FEAT-002 — it's what makes Success Metric #1 ("3 distinct projects, each owned by a distinct team, running concurrently") demonstrable.

**Acceptance Criteria:**
- [ ] Operator can create a team, invite a member, create a project with a `projectType`, add the member to the project with role(s) — end-to-end via REST.
- [ ] A non-operator member without `ProjectMembership` on project P receives `403 /probs/forbidden` from every project-scoped endpoint for P; audit row recorded.
- [ ] Soft-deleting a team that owns non-deleted projects → `409 /probs/conflict`.
- [ ] Soft-deleting a project soft-deletes its memberships in the same transaction.
- [ ] Adding a member already in the project → `409 /probs/conflict`.
- [ ] Removing the last operator → `409 /probs/conflict` ("at least one operator must remain").
- [ ] List endpoints support `page`, `pageSize`, `sortBy`, `sortDir`, basic filters, and return the standard `{ data, meta }` envelope.
- [ ] All mutation endpoints emit an audit entry inside the same transaction as the write.

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.Workspace/DTOs/*.cs` (TeamDto, CreateTeamRequest, MemberDto, InviteMemberRequest, ProjectDto, CreateProjectRequest, ProjectMembershipDto, AddMembershipRequest, RoleDto, plus paginated wrappers)
- Create: `src/DevHub.Modules.Workspace/Services/{Team,Member,Project,Membership,Role}Service.cs`
- Create: `src/DevHub.Modules.Workspace/Controllers/{Teams,Members,Projects,Memberships,Roles}Controller.cs`
- Modify: `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs` (register services)
- Create: `src/DevHub.Contracts/Pagination/PageRequest.cs`, `Pagination/PageMeta.cs`, `Envelope/PagedEnvelopeDto.cs`
- Modify: `src/DevHub.Modules.Workspace/Entities/Project.cs` (cache `IsArchived` query property — defer if not needed in v1)
- Modify: `src/DevHub.Modules.Identity/Services/AuthenticationService.cs` (replace stub `GetRoleKeysAsync` with `IProjectMembershipQuery`-backed lookup; JWT carries memberships)

**Technical Notes:**
Authorization first, every action. `Projects.Create` checks `Member.IsOperator` (system-role lookup). `Memberships.Add` checks operator AND validates the project's `projectType` resolves to an active `ExecutorBinding` (FEAT-003 lands the registry; until then, accept any non-empty `projectType` and emit a TODO log line). Cascade: deleting a Project sets `DeletedAt` on the Project and its non-deleted `ProjectMembership`+`RoleAssignment` rows in one transaction.

---

### T-025: Integration tests — grant + deny per endpoint, plus end-to-end happy path

**Type:** Testing · **Workflow:** standard · **Complexity:** L · **Dependencies:** T-024

**Description:**
Each Workspace endpoint ships with at least one grant-path and one deny-path integration test using the existing `DevHubApiFactory` + `PostgresFixture` from T-020. The happy-path "operator creates team → invites member → creates project → adds membership → member sees project" is a single end-to-end test class that proves Success Criterion AC-1 holds.

**Rationale:**
FEAT-001 set the discipline ("every façade endpoint requires a deny-path test"). FEAT-002 has ~10 mutation endpoints; each one needs both paths covered before merging.

**Acceptance Criteria:**
- [ ] Per-controller `*EndpointsTests` class with grant + deny per mutation.
- [ ] `WorkspaceWalkthroughTests.End_to_end_operator_flow_creates_visible_project` — full chain.
- [ ] Audit rows asserted via direct `AuditDbContext` query in at least one test per controller (grant + deny).
- [ ] `dotnet test` reports ≥40 passing tests across the Workspace + Audit projects (was 6 from T-020).

**Files to Modify/Create:**
- Create: `tests/DevHub.Modules.Workspace.Tests/{Teams,Members,Projects,Memberships,Roles}EndpointsTests.cs`
- Create: `tests/DevHub.Modules.Workspace.Tests/WorkspaceWalkthroughTests.cs`
- Create: `tests/DevHub.Modules.Workspace.Tests/Helpers/AuthenticatedClientHelpers.cs` (helper to log in as the seed operator + as a fresh non-operator)
- Modify: `tests/DevHub.TestHarness/DevHubApiFactory.cs` (allow override of seed-operator credentials per test)

**Technical Notes:**
The harness already gives us a per-test isolated DB. For deny tests, create a second member directly via the Workspace service (no API path for that yet — `IMemberLookup` extension or direct DbContext fixture helper). Audit assertions are simple Postgres queries through a fresh DbContext.

---

## Frontend

### T-026: Frontend HTTP layer + shared list/modal components

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** L · **Dependencies:** T-024

**Description:**
Stand up the SPA infrastructure every admin screen will reuse:
- `workspace.service.ts` — typed wrappers around the new endpoints (Signals + RxJS for HTTP; pagination state in component-scoped signals).
- `AppTable` — sortable, paginated table with column defs, row actions, empty/loading/error states (reuses `AppCard` shell).
- `AppModal` — accessible focus-trapped modal with header/body/footer slots.
- `ConfirmDialog` — opinionated wrapper around `AppModal` for destructive actions.

**Rationale:**
Five admin screens + two project screens land in T-027 and T-028; without shared components they'd diverge. Mockup-first per CLAUDE.md routing.

**Acceptance Criteria:**
- [ ] Mockup at `mockups/app-table.html` covering: default + loading skeleton + empty + error + paginated states.
- [ ] `AppTable` accepts a column-defs array (header, cell template, sortable bool) and a paged-data signal; exposes `sortChanged` + `pageChanged` outputs.
- [ ] `AppModal` traps focus, restores it on close, closes on Escape and overlay click (opt-in).
- [ ] `ConfirmDialog` accepts `title`/`message`/`confirmLabel`/`variant` (default `danger`); emits `confirmed`/`cancelled`.
- [ ] `workspace.service.ts` ships typed methods for every endpoint in `api-spec.md` §Workspace; unwraps the `{ data, meta }` envelope.
- [ ] Specs pass for `AppTable` (column rendering, sort emit, page emit, empty state), `AppModal` (focus trap, escape, overlay), `ConfirmDialog` (confirm/cancel paths).

**Files to Modify/Create:**
- Create: `client/src/app/shared/components/app-table/*`
- Create: `client/src/app/shared/components/app-modal/*`
- Create: `client/src/app/shared/components/confirm-dialog/*`
- Modify: `client/src/app/shared/index.ts`
- Create: `client/src/app/core/api/workspace.service.ts`, `workspace.types.ts`
- Create: `mockups/app-table.html`

**Technical Notes:**
Pagination is offset-based per `api-spec.md`. The table's filter UI is owned by the parent screen; `AppTable` only handles sort + page. Focus trap for AppModal: maintain a list of focusable selectors and cycle with Tab/Shift+Tab; use `inert` on the rest of the app while open.

---

### T-027: Project screens — list + project home (no work-items table yet)

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M · **Dependencies:** T-026

**Description:**
Replace the FEAT-001 home-page placeholder for "Your projects" with a real card grid (`/projects`), and ship the project home (`/projects/:slug`) per `docs/ui-specification.md` §3-4. The work-item table on Project home lands in FEAT-004; for now Project home shows the project header, the team owner, project-type chip, and a stub empty state.

**Rationale:**
Members need to be able to see and open their projects after FEAT-002 lands; without this UI, the API surface is invisible.

**Acceptance Criteria:**
- [ ] `/projects` (route guard: authenticated) renders `ProjectGrid` cards from `GET /api/projects?sortBy=updatedAt`.
- [ ] Filtering by team and projectType updates the list; loading shows skeleton cards.
- [ ] Clicking a project card navigates to `/projects/:slug`.
- [ ] `/projects/:slug` reads the project from `GET /api/projects/{slug}` (or by id if slug-routes land later); 404 → friendly "Project not found" page that links back to `/projects`.
- [ ] 403 on `/projects/:slug` → friendly forbidden page ("You don't have access to this project").
- [ ] Mockups at `mockups/project-list.html` and `mockups/project-home.html`.

**Files to Modify/Create:**
- Create: `client/src/app/features/projects/project-list.page.{ts,html,spec.ts}`
- Create: `client/src/app/features/projects/project-home.page.{ts,html,spec.ts}`
- Create: `client/src/app/features/projects/project-card.{ts,html,spec.ts}`
- Modify: `client/src/app/app.routes.ts` (add /projects and /projects/:slug)
- Modify: `client/src/app/features/home/home.page.html` (remove "No projects yet" placeholder once T-027 wires the real grid; Home shows a "Browse all" link to /projects)
- Create: `mockups/project-list.html`, `mockups/project-home.html`

**Technical Notes:**
Routing uses the project slug (`/projects/:slug`) for nicer URLs; the API accepts slug or id (T-024 adds the slug lookup endpoint). The 403 page is the same template as the "Forbidden" empty state from `docs/ui-specification.md` §State Patterns.

---

### T-028: Admin screens — Teams, Members, Project memberships (operator-only)

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** L · **Dependencies:** T-026

**Description:**
Three operator-only CRUD screens behind a `[role=operator]` route guard:
- `/admin/teams` — list/create/edit/delete teams (delete is soft).
- `/admin/members` — list/invite/edit/delete members.
- `/projects/:slug/admin/memberships` — list/add/edit/remove memberships for a project, including role assignments.

Each screen uses `AppTable` + `AppModal` + `ConfirmDialog` from T-026 and unwraps the `{ data, meta }` envelope from `workspace.service.ts`.

**Rationale:**
Without these screens, operators have to hit the API directly to seed teams/members/memberships — defeating the front-door rule.

**Acceptance Criteria:**
- [ ] Non-operator caller is redirected away from any `/admin/*` route by the `operatorGuard`.
- [ ] Each screen renders an empty state when the list is empty.
- [ ] Create/edit modals submit via `workspace.service.ts`; failures surface the typed `AppError` in the modal body (not as a toast).
- [ ] Delete confirms via `ConfirmDialog` and surfaces 409 conflicts inline ("Cannot delete: team owns 2 projects").
- [ ] Memberships screen exposes role checkboxes (multi-select); roles list is fetched once on mount via `GET /api/roles`.
- [ ] Mockups at `mockups/admin-teams.html`, `mockups/admin-members.html`, `mockups/project-memberships.html`.

**Files to Modify/Create:**
- Create: `client/src/app/features/admin/teams/teams.page.{ts,html,spec.ts}`
- Create: `client/src/app/features/admin/teams/team-form.modal.{ts,html,spec.ts}`
- Create: `client/src/app/features/admin/members/members.page.{ts,html,spec.ts}`
- Create: `client/src/app/features/admin/members/member-form.modal.{ts,html,spec.ts}`
- Create: `client/src/app/features/projects/memberships/memberships.page.{ts,html,spec.ts}`
- Create: `client/src/app/features/projects/memberships/membership-form.modal.{ts,html,spec.ts}`
- Create: `client/src/app/core/auth/operator.guard.{ts,spec.ts}`
- Modify: `client/src/app/app.routes.ts`
- Modify: `client/src/app/core/layouts/app-shell/sidebar.html` (link the three admin entries to real routes)
- Create: `mockups/admin-teams.html`, `mockups/admin-members.html`, `mockups/project-memberships.html`

**Technical Notes:**
The `operatorGuard` reuses `AuthService.isOperator` (already implemented in T-013/T-014). Form modals follow the same pattern: a single `FormGroup` driven by the modal's input record; validation errors render via `AppFormField` (already wired).

---

## Summary

| Group | Count | Tasks |
|-------|-------|-------|
| Backend | 4 | T-022, T-023, T-024, T-025 |
| Frontend | 3 | T-026, T-027, T-028 |
| **Total** | **7** | |

**Complexity:** S=1, M=1, L=4, XL=1.

**Critical path:** T-022 → T-023 → T-024 → T-025. Frontend (T-026..T-028) parallelizes after T-024.

**Risk register:**
- **Operator-status determination** — workspace-global means the member needs a way to be marked "operator" outside of `ProjectMembership`. T-023's plan offers two options; T-024 commits to one. Likely: workspace-level role assignment with a sentinel `projectId = null` (cleanest), or a `Member.IsSystemOperator` boolean (simplest). Decide in T-024.
- **ExecutorBinding coupling** — `Projects.Create` should reject an unknown `projectType` per FEAT-003 AC-2. Until FEAT-003 lands the registry, T-024 logs a TODO and accepts any non-empty string. Document this in the FEAT-003 migration plan.
- **Audit consistency** — T-022's `IAuditWriter` is convenient, but only T-024's reviewers can verify every mutation actually calls it. Mitigate with an integration test per controller that asserts audit row presence (T-025 covers this).

## Post-Generation Checklist

- [x] All FEAT-002 acceptance criteria covered (AC-1↔T-024+T-027, AC-2↔T-023+T-025, AC-3↔T-024 walkthrough, AC-4↔T-024 service rules, AC-5↔T-023+T-025, AC-6↔T-025).
- [x] Audit module precedes Workspace mutations (T-022 → T-024).
- [x] Authorization service precedes endpoints (T-023 → T-024).
- [x] Each frontend task has a mockup-first prerequisite.
- [x] Dependency graph is acyclic.
- [x] No task violates the Stakeholder scope lock.
