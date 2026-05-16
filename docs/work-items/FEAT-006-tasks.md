# FEAT-006 Task Breakdown — Operator Dashboard & Audit Log

> Generated from `docs/work-items/FEAT-006-operator-dashboard-audit.md` using `.ai-framework/prompts/feature-tasks.md`. 4 tasks across Backend, Testing, and Frontend.

## Scope choices locked in before generation

- **AC-1 / AC-2 are already 95% met.** T-022 landed `AuditEntry` + `IAuditWriter`; every mutation across Workspace / ExecutorRegistry / WorkItems / Notifications already writes Granted (and Denied / Failed) rows inside its transaction. T-047 audits the codebase to confirm "every mutation writes one row, every deny writes one Denied row" and adds explicit test coverage that fails when someone adds a non-audited mutation later.
- **AC-5 enforcement, hybrid: code-level + test-level.** `AuditWriter` only `Add`s — never `Update`s, never `Remove`s. T-047 ships a static grep-style test that scans the codebase for `AuditEntries.Update`, `AuditEntries.Remove`, `ExecuteUpdate.*audit`, `ExecuteDelete.*audit` and fails on any match. v2 may add a Postgres role with `INSERT`-only grant on `audit.audit_entries`; v1 leaves the runtime grant at standard since tests need to read.
- **Append-only invariant in code.** `AuditDbContext` doesn't expose any update path; we add an `IAuditQueryService` that's read-only and registered with `AsNoTracking()` queries only. No service in any module retains a tracked `AuditEntry` after creation.
- **Project audit auth — Project:any (any membership, no specific role).** Existing pattern: `IProjectAuthorizationService.EnsureAuthorizedAsync(memberId, projectId, "audit:read", requiredRoleKey: null)`.
- **Operator dashboard aggregations: client-side over existing endpoints.** UI Spec §7 says so explicitly. v1 uses `GET /api/projects` (already returns `inFlightWorkItems`), `GET /api/notifications/pending` (the operator sees every pending checkpoint workspace-wide via the reconciler — proven in T-045), and the new `GET /api/admin/audit?outcome=Denied,Failed`. No new backend for the dashboard.
- **Audit DTO shape** follows api-spec.md §AuditEntryDto: `id`, `occurredAt`, `actingMember: { id, displayName }?`, `projectId?`, `targetType`, `targetId?`, `action`, `outcome`, `reason?`, `details: { ... }?` (parsed from `details_json`).
- **Audit filters**: `actingMemberId`, `targetType`, `action`, `outcome`, `from`, `to` for the project endpoint; admin endpoint adds `projectId`. All optional; default sort `occurredAt desc`.

---

## Backend

### T-047: Audit query service + AC-1/AC-2/AC-5 invariant tests

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-022 (audit module), every prior FEAT (mutation coverage)

**Description:**
Publish `IAuditQueryService` from `DevHub.Modules.Audit` with the filter+page contract. Implement it over `AuditDbContext` with `AsNoTracking()` reads only. Add invariant tests that codify:
- AC-5: no `AuditEntries.Update(...)` / `.Remove(...)` / `ExecuteUpdate*` / `ExecuteDelete*` against the audit table anywhere in `src/`.
- AC-1: a sweep test that exercises every known mutation surface (operator-create-team, invite-member, register-executor, start-work-item, signal, cancel, add-membership, etc.) and asserts each one produces a Granted audit row.
- AC-2: a sweep test that exercises one deny path per mutation surface and asserts each produces a Denied row with a non-empty `reason`.

**Rationale:**
The bulk of audit writing already exists. What's missing is the read-side AND the codified invariant tests that catch regressions when a future module adds a mutation but forgets to audit.

**Acceptance Criteria:**
- [ ] `IAuditQueryService.ListAsync(filter, page, ct)` and `ListForProjectAsync(projectId, filter, page, ct)` return paged DTOs ordered by `occurred_at desc`.
- [ ] Filter shape supports `actingMemberId`, `targetType`, `action`, `outcome`, `from`, `to`; admin variant also supports `projectId`. Empty filter returns everything.
- [ ] `IAuditQueryService` is registered scoped; consumes only `AuditDbContext` and never tracks entities.
- [ ] **`AppendOnlyAuditInvariantTests`** (static grep test in `tests/DevHub.Modules.Audit.Tests/`): walks every `.cs` file under `src/` and fails if any line matches `AuditEntries\.(Update|Remove|RemoveRange)` or `audit_entries.*ExecuteUpdate|audit_entries.*ExecuteDelete`. The test is intentionally simple — false positives are easier to catch than missing writes.
- [ ] **`AuditMutationSweepTests`** (integration, real Postgres + WAF): exercises ≥6 mutation endpoints (team:create, member:invite, executor:create, project:create, workitem:start, signal) and asserts each produces a Granted row keyed by the action string.
- [ ] **`AuditDenySweepTests`** (integration): exercises a deny path per mutation surface (non-operator team:create, non-member workitem:start, etc.) and asserts each produces a Denied row with `reason` set.

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.Audit/DTOs/AuditEntryDto.cs`, `AuditFilter.cs`
- Create: `src/DevHub.Modules.Audit/Services/IAuditQueryService.cs`, `AuditQueryService.cs`
- Modify: `src/DevHub.Modules.Audit/AuditModuleExtensions.cs` (register `IAuditQueryService` as scoped + the existing `IAuditWriter`)
- Create: `tests/DevHub.Modules.Audit.Tests/AppendOnlyAuditInvariantTests.cs`
- Create: `tests/DevHub.Modules.Audit.Tests/AuditMutationSweepTests.cs`
- Create: `tests/DevHub.Modules.Audit.Tests/AuditDenySweepTests.cs`
- Modify: `tests/DevHub.Modules.Audit.Tests/DevHub.Modules.Audit.Tests.csproj` (refs to Workspace + WorkItems + Notifications + ExecutorRegistry for cross-module helpers)

**Technical Notes:**
The query service should join `acting_member_id` to Workspace's `IMemberLookup` to surface `displayName`. v1 ships per-row lookups (acceptable for typical "≤500 entries per page" loads); v2 batches.

For the invariant test: `Directory.GetFiles("src", "*.cs", SearchOption.AllDirectories)` then per-file regex scan. Skip migration files (`Migrations/`) — those are EF-generated against the AuditEntry table for INSERT only at `Initial`; future migrations are accepted as long as they don't drop or rewrite audit data.

The `details_json` column is `jsonb`. The DTO exposes it as `JsonElement` (or `JsonDocument` parsed lazily) — opaque to the API.

---

### T-048: Audit endpoints + integration tests

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-047

**Description:**
Two thin controllers backed by `IAuditQueryService`:
- `GET /api/projects/{projectId}/audit` — `project:any`, audited (the auth check itself writes an audit row).
- `GET /api/admin/audit` — operator only, audited.

Both honor the filter set. Integration tests cover scoping (AC-4), filter correctness, and pagination.

**Rationale:**
The read-side surface that makes the audit log actually useful for operators + project members. AC-3 dashboards depend on this for the "recent failures" panel.

**Acceptance Criteria:**
- [ ] `GET /api/projects/{projectId}/audit` returns the project-scoped page envelope; non-member → 403 with a Denied audit row (recursive but bounded — the "audit:read" denial doesn't trigger another read).
- [ ] `GET /api/admin/audit` returns the cross-project page; non-operator → 403 with a Denied row.
- [ ] All filter params parsed and applied; default sort `occurredAt desc`; max pageSize capped at 200.
- [ ] Soft-deleted projects still return their audit history (project_id reference is preserved per business rule).
- [ ] Both endpoints are read-only — no mutation, no executor call.

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.Audit/Controllers/AuditController.cs` (project + admin actions on one controller, distinguished by route)
- Modify: `src/DevHub.Modules.Audit/DevHub.Modules.Audit.csproj` (add `FrameworkReference Microsoft.AspNetCore.App` if not present; drop redundant Configuration/Hosting abstractions packages)
- Verify: `src/DevHub.Api/Program.cs` `AddApplicationPart(typeof(AuditDbContext).Assembly)` already wires the new controller
- Create: `tests/DevHub.Modules.Audit.Tests/AuditEndpointsTests.cs`

**Technical Notes:**
The project-audit auth uses the existing `IProjectAuthorizationService.EnsureAuthorizedAsync(memberId, projectId, "audit:read", null)` — same shape as workitem-read. Cross-project audit uses `EnsureOperatorAsync(memberId, "audit:read:admin")`.

Pagination uses the existing `PageRequest.Normalize()` from Contracts; cap pageSize at 200 in the controller (don't let a client request 10,000 audit rows).

`AuditEndpointsTests` re-uses the FEAT-002/004/005 test helpers (project + work item + signal flow) to seed a diverse set of audit rows, then asserts filter behavior.

---

## Frontend

### T-049: Audit log screens (project + admin)

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M · **Dependencies:** T-048

**Description:**
Two related screens sharing components:
- `/projects/:slug/audit` — project-scoped audit, accessible to any project member.
- `/operator/audit` — cross-project audit, behind `operatorGuard`.

Both use a shared `AuditTable` with the filter bar above. Each row shows occurredAt, actingMember (or "system"), target, action, outcome pill (Granted=emerald, Denied=red, Failed=amber). Clicking a row expands `details_json` inline (pretty-printed).

**Rationale:**
UI Spec §8. Project members need visibility into project authorization decisions; operators need workspace-wide.

**Acceptance Criteria:**
- [ ] Mockup at `mockups/audit-log.html` covering: empty state, populated table (mix of outcomes), expanded row with `details_json` JSON tree, filter bar (acting member dropdown, action, outcome pill bar, date range), 403 forbidden state.
- [ ] `AuditService` ships typed `listProject(projectId, filter)` and `listAdmin(filter)` methods over the new endpoints.
- [ ] `AuditTable` is a reusable component (renders rows, handles row expand, paginates).
- [ ] Project route: `/projects/:slug/audit` — replaces the FEAT-002 aria-disabled "Audit" tab placeholder on Project home with a real link.
- [ ] Admin route: `/operator/audit` — operator-guarded.
- [ ] Filter bar emits debounced (250ms) state changes that re-query.
- [ ] Specs cover: page renders rows after parallel load, outcome pill classes, filter change triggers refetch, row expand toggles details, 403 surface.

**Files to Modify/Create:**
- Create: `client/src/app/core/api/audit.{service,types}.ts`
- Create: `client/src/app/features/audit/audit-table.{ts,html,spec.ts}` (shared component)
- Create: `client/src/app/features/audit/audit.types.ts`
- Create: `client/src/app/features/audit/project-audit.page.{ts,html,spec.ts}` at `/projects/:slug/audit`
- Create: `client/src/app/features/audit/operator-audit.page.{ts,html,spec.ts}` at `/operator/audit`
- Modify: `client/src/app/app.routes.ts` (two new routes)
- Modify: `client/src/app/features/projects/project-home.page.html` (link the "Audit" tab to the new project audit page; remove the aria-disabled placeholder)
- Create: `mockups/audit-log.html`

**Technical Notes:**
`AuditTable` accepts an `Input<AuditEntryDto[]>`, `loading`, `meta`, `filter` and emits `filterChanged` + `pageChanged`. Both pages wrap it with the right header copy and the appropriate `AuditService` method.

Outcome pill colors:
- `Granted` → `bg-emerald-50 text-emerald-700`
- `Denied` → `bg-red-50 text-red-700`
- `Failed` → `bg-amber-50 text-amber-800`

Row expand: signal-backed `Set<rowId>`; clicking the row toggles inclusion. Expanded content renders the `details` blob via the existing `ExecutorStatePanel` (from T-040) or a new `JsonTree` helper — pick whichever is cleaner.

---

### T-050: Operator dashboard

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M · **Dependencies:** T-048, T-049 (AuditService), T-046 (PendingActionsStore)

**Description:**
`/operator` route, operator-guarded. Three panels per UI Spec §7:
1. **In-flight totals** — sum of `inFlightWorkItems` across all projects, plus per-project breakdown bar.
2. **Pending approvals (grouped by project)** — derived client-side from the existing `PendingActionsStore` (the operator already sees every workspace pending checkpoint via the FEAT-005 reconciler).
3. **Recent audit events** — last 50 entries with `outcome ∈ {Denied, Failed}`, via `AuditService.listAdmin({ outcome: ['Denied','Failed'] })`.

No new backend endpoints — pure client-side aggregation of existing APIs.

**Rationale:**
UI Spec §7. The "routing-layer replacement" — operators see in one place what they previously hunted for in chat.

**Acceptance Criteria:**
- [ ] Mockup at `mockups/operator-dashboard.html` covering: header with refresh, in-flight totals card (big number + per-project breakdown), pending-approvals card (grouped by project, click-through links to the review page), recent audit events card (chronological list with outcome pills).
- [ ] `/operator` route guarded by `operatorGuard`; non-operator is redirected.
- [ ] Page uses `max-w-7xl` shell variant (wider than the default `max-w-5xl`) per UI Spec.
- [ ] Loads in parallel: `WorkspaceService.listProjects({pageSize: 100})`, `AuditService.listAdmin({ outcome: ['Denied','Failed'], pageSize: 50 })`. Pending approvals read from the live `PendingActionsStore` (no separate fetch).
- [ ] Refresh button re-pulls all three panels.
- [ ] Specs cover: page renders all three panels after parallel load, refresh triggers re-pull, empty states per panel.
- [ ] Sidebar gains a real `/operator` link (replaces the existing placeholder if present).

**Files to Modify/Create:**
- Create: `client/src/app/features/operator/operator-dashboard.page.{ts,html,spec.ts}` at `/operator`
- Modify: `client/src/app/app.routes.ts` (add `/operator` route)
- Modify: `client/src/app/core/layouts/app-shell/sidebar.html` (link the operator group's Dashboard / Audit log entries to the real routes — they're currently RouterLinks to `/operator` and `/operator/audit` but those routes don't exist yet)
- Modify: `client/src/app/core/layouts/app-shell/app-shell.html` (relax the `max-w-5xl` wrapper to allow the dashboard route to opt into `max-w-7xl` — simplest path is a per-route style hook or just override on the dashboard's outer `<main>` wrapper from inside the component)
- Create: `mockups/operator-dashboard.html`

**Technical Notes:**
The `max-w-5xl` shell wrap in `app-shell.html` is uniform today; the dashboard wants `max-w-7xl`. v1 option: have the dashboard page render its own `max-w-7xl mx-auto` block and rely on the outer `max-w-5xl` becoming visually a no-op (the inner is wider). Actually that won't work — the outer constrains. Cleaner: refactor `app-shell.html` to remove the `max-w-5xl` constraint and have each page apply its own width. Defer to v2 if scope creeps; for v1 ship a `max-w-7xl` override using a CSS escape (`!important` is OK on a single block here).

The audit events panel re-uses `AuditTable` from T-049 in a "compact" variant (no filters, fixed page size).

---

## Summary

| Group | Count | Tasks |
|-------|-------|-------|
| Backend | 2 | T-047, T-048 |
| Frontend | 2 | T-049, T-050 |
| **Total** | **4** | |

**Complexity:** M=4.

**Critical path:** T-047 → T-048 → T-049 → T-050. T-050 needs T-049's `AuditService` for the recent-events panel.

**Risk register:**
- **Static grep test brittleness.** `AppendOnlyAuditInvariantTests` is a regex scan. If a future module legitimately needs to update an audit row (e.g., backfill `acting_member_id` correction), the test will fail loudly — that's the desired behavior. Document on the test: "if you have a legitimate reason to mutate audit rows, write a migration with explicit justification, then update this allowlist."
- **Audit row volume in dev databases.** Tests create thousands of audit rows over a session. Per-test isolated DBs (FEAT-001 harness) keep this bounded. Production volume is operator-visible; FEAT-006+ can ship an archive task.
- **Recursive deny audit.** `EnsureAuthorizedAsync` on the audit-read endpoint writes a Denied row when access is denied. That row's existence is visible to operators only (the denied member can't see it because they were just denied) — exactly the desired surface.
- **Project soft-delete preserves audit.** Already covered by the data model (project_id is FK-by-id, not nav property). Acceptance Verification in T-048 includes a test.
- **Audit pageSize cap.** 200 is arbitrary; tune as needed. Document.
- **Operator dashboard refresh storm.** Refresh button = 2 HTTP calls + 1 store sync. Acceptable.

## Post-Generation Checklist

- [x] All FEAT-006 ACs map to specific tasks:
  - AC-1 ↔ T-047 (sweep tests)
  - AC-2 ↔ T-047 (deny sweep)
  - AC-3 ↔ T-050 (dashboard panels)
  - AC-4 ↔ T-048 (endpoint scoping tests)
  - AC-5 ↔ T-047 (grep invariant test)
- [x] Read-side service published before consumers (T-047 → T-048).
- [x] Endpoints before frontend (T-048 → T-049 → T-050).
- [x] Each frontend task is mockup-first.
- [x] Dependency graph is acyclic.
- [x] No task violates the Stakeholder scope lock (no velocity charts, no bulk export, no v2 RBAC on audit reads).
