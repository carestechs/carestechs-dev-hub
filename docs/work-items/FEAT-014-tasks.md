# FEAT-014 Tasks — Project Documentation

---

## Group 1 — Foundation

### T-001: ProjectDoc entity + migration

**Type:** Database  
**Workflow:** standard  
**Complexity:** S  
**Dependencies:** None

**Description:**  
Add the `project_docs` table to the Workspace module. Create the `ProjectDoc` EF Core entity and migration with columns `id`, `project_id`, `doc_key`, `content`, `updated_at`, `updated_by_id`. Add a unique constraint on `(project_id, doc_key)` and a cascade-delete FK to `projects`.

**Rationale:**  
Foundational storage for all seven framework documents. Must land before any service or API work.

**Acceptance Criteria:**
- [ ] `project_docs` table created with all specified columns and constraints
- [ ] EF Core migration applies cleanly on a fresh database
- [ ] Cascade delete from `projects` removes all related `project_docs` rows

**Files to Modify/Create:**
- `src/DevHub.Modules.Workspace/Entities/ProjectDoc.cs` — new entity
- `src/DevHub.Modules.Workspace/WorkspaceDbContext.cs` — add `DbSet<ProjectDoc>`
- `src/DevHub.Modules.Workspace/Migrations/` — new EF migration

**Technical Notes:**  
`doc_key` is `varchar(80)`. `content` is `text` (no length limit). `updated_by_id` is nullable FK to allow system-seeded rows in the future.

---

### T-002: DocKey enum + valid-key constants

**Type:** Backend  
**Workflow:** standard  
**Complexity:** S  
**Dependencies:** T-001

**Description:**  
Define the seven valid `doc_key` values as a static class or enum in the Workspace module. Add a validation helper used by both the service and controller to reject unknown keys with a 400.

**Rationale:**  
Centralises the hard-coded key list so it is not scattered across service, controller, and tests.

**Acceptance Criteria:**
- [ ] Seven keys defined: `stakeholder-definition`, `architecture`, `data-model`, `api-spec`, `ui-specification`, `primary-user-persona`, `claude-md`
- [ ] A `label` and `description` string is paired with each key (used in the API list response and the UI editor hint)
- [ ] Unknown key submitted to any endpoint returns 400

**Files to Modify/Create:**
- `src/DevHub.Modules.Workspace/Domain/ProjectDocKeys.cs` — new static class with keys, labels, descriptions

---

## Group 2 — Backend

### T-003: ProjectDocsService + IProjectDocsService

**Type:** Backend  
**Workflow:** standard  
**Complexity:** M  
**Dependencies:** T-001, T-002

**Description:**  
Implement `IProjectDocsService` with three methods: `ListAsync` (returns all seven keys with metadata, filling nulls for missing rows), `GetAsync` (returns content + metadata for one key), and `UpsertAsync` (creates or updates content, writes audit entry).

**Rationale:**  
Service layer owns all business logic — key validation, empty-content treatment (whitespace = unfilled), authorization checks delegated to the controller, audit writes.

**Acceptance Criteria:**
- [ ] `ListAsync` always returns exactly seven items regardless of how many rows exist
- [ ] Items with no DB row have `filledAt: null` and `content: null`
- [ ] Whitespace-only content is treated as unfilled (normalised to null on upsert)
- [ ] `UpsertAsync` writes an `AuditEntry` with action `project:doc-updated`
- [ ] `GetAsync` throws `NotFoundException` for unknown project or unknown key

**Files to Modify/Create:**
- `src/DevHub.Modules.Workspace/Services/ProjectDocsService.cs` — new service
- `src/DevHub.Modules.Workspace/DTOs/ProjectDocDtos.cs` — `ProjectDocSummaryDto`, `ProjectDocDto`, `UpsertDocRequest`

---

### T-004: ProjectDocsController (REST endpoints)

**Type:** Backend  
**Workflow:** standard  
**Complexity:** M  
**Dependencies:** T-003

**Description:**  
Add `ProjectDocsController` with three endpoints: `GET /projects/{id}/docs`, `GET /projects/{id}/docs/{key}`, `PUT /projects/{id}/docs/{key}`. Enforce `project:read` auth on GETs and operator role on PUT. Resolve the current member from JWT on all actions.

**Rationale:**  
Exposes the doc sub-resource via the REST envelope pattern used by all other Workspace endpoints.

**Acceptance Criteria:**
- [ ] `GET /projects/{id}/docs` returns `{ data: ProjectDocSummaryDto[] }` — all seven keys
- [ ] `GET /projects/{id}/docs/{key}` returns `{ data: ProjectDocDto }` including full content
- [ ] `PUT /projects/{id}/docs/{key}` accepts `{ content: string }`, returns updated `ProjectDocDto`
- [ ] Unknown `{key}` → 400; unknown `{id}` → 404; non-operator PUT → 403
- [ ] All endpoints return RFC 7807 problem details on error

**Files to Modify/Create:**
- `src/DevHub.Modules.Workspace/Controllers/ProjectDocsController.cs` — new controller

---

### T-005: Work item creation gate

**Type:** Backend  
**Workflow:** standard  
**Complexity:** S  
**Dependencies:** T-003

**Description:**  
In `WorkItemService.CreateAsync` (inside `DevHub.Modules.WorkItems`), before forwarding to the executor, call `IProjectDocsService.ListAsync` and verify all seven docs are filled. If any are unfilled, throw a `ConflictException` that maps to `409` with `type: /probs/project-docs-incomplete` and a `missingDocs` list.

**Rationale:**  
Enforces the hard rule that no work can start until the project is fully documented. The check lives in the WorkItems module (cross-module call via interface in `DevHub.Contracts`).

**Acceptance Criteria:**
- [ ] `POST /projects/{id}/work-items` returns 409 when any doc is unfilled
- [ ] Response body includes `{ missingDocs: ["stakeholder-definition", ...] }`
- [ ] Request succeeds normally when all seven docs are filled
- [ ] Gate check is the first non-validation step in `CreateAsync`

**Files to Modify/Create:**
- `src/DevHub.Contracts/IProjectDocsQuery.cs` — new interface exposing `AllFilledAsync(projectId, ct) → (bool, string[])`
- `src/DevHub.Modules.Workspace/Services/ProjectDocsService.cs` — implement `IProjectDocsQuery`
- `src/DevHub.Modules.WorkItems/Services/WorkItemService.cs` — inject `IProjectDocsQuery`, add gate
- `src/DevHub.Api/Program.cs` — register `IProjectDocsQuery → ProjectDocsService`

**Technical Notes:**  
`IProjectDocsQuery` is the cross-module contract (lives in `DevHub.Contracts`). `ProjectDocsService` implements both `IProjectDocsService` (internal Workspace) and `IProjectDocsQuery` (Contracts). This avoids any direct Workspace → WorkItems coupling.

---

## Group 3 — Frontend

### T-006: Angular types + ProjectDocsService

**Type:** Frontend  
**Workflow:** standard  
**Complexity:** S  
**Dependencies:** T-004

**Description:**  
Add `ProjectDocSummaryDto`, `ProjectDocDto`, and `UpsertDocRequest` TypeScript types. Create `ProjectDocsService` that wraps the three API endpoints.

**Rationale:**  
Client-side API layer before any UI component can consume it.

**Acceptance Criteria:**
- [ ] Types match the backend DTOs exactly
- [ ] `ProjectDocsService` exposes `listDocs(projectId)`, `getDoc(projectId, key)`, `upsertDoc(projectId, key, content)` as `Promise`-returning methods
- [ ] Service is `providedIn: 'root'`

**Files to Modify/Create:**
- `client/src/app/core/api/project-docs.types.ts` — new types
- `client/src/app/core/api/project-docs.service.ts` — new service

---

### T-007: Docs tab on project home page + incomplete banner

**Type:** Frontend  
**Workflow:** mockup-first  
**Complexity:** M  
**Dependencies:** T-006

**Description:**  
Add a "Docs" tab to the project home page navigation. When the tab is active, render the doc list (seven cards with filled/empty status badge). Add an amber banner above the tab bar (operator-only) that lists unfilled docs when any are missing. The banner should also appear on the Work Items tab so it is visible before the user discovers the gate.

**Rationale:**  
Makes doc completeness visible at a glance on the project's main page and prompts operators to act before they hit the work-item gate. Mockup needed because this is a new screen layout with a status-driven card list.

**Acceptance Criteria:**
- [ ] "Docs" tab appears in project nav alongside "Work items" and "Audit"
- [ ] Tab shows seven doc cards: label, description hint, filled/empty badge, edit/view button
- [ ] Filled badge is emerald; empty badge is amber
- [ ] Amber incomplete banner shown to operators on both tabs when any doc is empty
- [ ] Banner lists the unfilled doc labels (not keys)
- [ ] Non-operators see the tab but no edit buttons and no banner

**Files to Modify/Create:**
- `client/src/app/features/projects/project-home.page.html` — add Docs tab + banner
- `client/src/app/features/projects/project-home.page.ts` — load docs, compute incomplete signal
- `client/src/app/features/projects/docs/project-docs-tab.component.ts` — new component
- `client/src/app/features/projects/docs/project-docs-tab.component.html` — new template
- `mockups/FEAT-014-project-docs-tab.html` — mockup (generated first)

---

### T-008: Doc editor page

**Type:** Frontend  
**Workflow:** mockup-first  
**Complexity:** M  
**Dependencies:** T-006

**Description:**  
Create a full-page doc editor at `/projects/{slug}/docs/{key}`. Shows the doc label, a description/hint of what to fill in, and a plain `<textarea>` pre-populated with existing content. Operators see Save + Cancel buttons; non-operators see read-only content. Save calls `upsertDoc` and navigates back to the Docs tab.

**Rationale:**  
Dedicated editor page keeps the form large enough to write meaningful docs without cramping into a modal or side panel. Mockup needed because it's a new full-page layout.

**Acceptance Criteria:**
- [ ] Route `/projects/:slug/docs/:key` renders the editor
- [ ] Unknown `key` in URL shows a "Document not found" inline error
- [ ] Operator: textarea enabled, Save + Cancel buttons present
- [ ] Non-operator: textarea `readonly`, no Save button
- [ ] Save shows loading state, navigates back to Docs tab on success
- [ ] Cancel navigates back without API call
- [ ] Unsaved changes prompt (browser `beforeunload`) when content has been modified

**Files to Modify/Create:**
- `client/src/app/features/projects/docs/project-doc-editor.page.ts` — new component
- `client/src/app/features/projects/docs/project-doc-editor.page.html` — new template
- `client/src/app/app.routes.ts` — register route
- `mockups/FEAT-014-project-doc-editor.html` — mockup (generated first)

---

### T-009: Work item start gate — UI feedback

**Type:** Frontend  
**Workflow:** standard  
**Complexity:** S  
**Dependencies:** T-007, T-008

**Description:**  
Handle the `409 /probs/project-docs-incomplete` response in the Start Work modal. Show a clear inline error listing the unfilled docs with a link to the Docs tab, instead of the generic error banner.

**Rationale:**  
Without this, the operator hits a generic 409 error with no actionable guidance. The specific error response from T-005 carries enough data to show a meaningful message.

**Acceptance Criteria:**
- [ ] `409` with type `/probs/project-docs-incomplete` renders a named error (not the generic banner)
- [ ] Error message names the missing docs
- [ ] A "Go to Docs tab" link is shown that closes the modal and activates the Docs tab

**Files to Modify/Create:**
- `client/src/app/features/projects/work-items/components/start-work-modal.ts` — detect specific 409
- `client/src/app/features/projects/work-items/components/start-work-modal.html` — render docs-incomplete state

---

## Group 4 — Testing

### T-010: Backend integration tests for ProjectDocsController + work item gate

**Type:** Testing  
**Workflow:** standard  
**Complexity:** M  
**Dependencies:** T-004, T-005

**Description:**  
Integration tests (Postgres-backed) covering: list returns seven items with nulls for missing, upsert creates/updates, operator-only write enforced, work item creation blocked when any doc missing, work item creation succeeds when all seven filled.

**Rationale:**  
The work item gate is a hard business rule — it must be tested for both block and pass paths. Authorization deny paths are required by CLAUDE.md convention.

**Acceptance Criteria:**
- [ ] `GET /projects/{id}/docs` returns 7 items with correct null/filled state
- [ ] `PUT /projects/{id}/docs/{key}` as non-operator → 403
- [ ] `PUT /projects/{id}/docs/{key}` with unknown key → 400
- [ ] `POST /projects/{id}/work-items` with 0 filled docs → 409 with `missingDocs` length 7
- [ ] `POST /projects/{id}/work-items` with 6/7 docs filled → 409 with `missingDocs` length 1
- [ ] `POST /projects/{id}/work-items` with all 7 filled → succeeds (forwarded to executor)

**Files to Modify/Create:**
- `tests/DevHub.Modules.Workspace.Tests/ProjectDocsTests.cs` — new test class
- `tests/DevHub.Modules.WorkItems.Tests/WorkItemCreateDocsGateTests.cs` — new test class

---

## Summary

| Group | Tasks | Types |
|-------|-------|-------|
| Foundation | T-001, T-002 | Database, Backend |
| Backend | T-003, T-004, T-005 | Backend |
| Frontend | T-006, T-007, T-008, T-009 | Frontend |
| Testing | T-010 | Testing |

**Complexity distribution:** S × 4, M × 5, L × 0

**Critical path:** T-001 → T-002 → T-003 → T-004 → T-005 → T-010  
Frontend path: T-004 → T-006 → T-007 → T-008 → T-009

**Mockup-first tasks:** T-007 (Docs tab), T-008 (Doc editor page) — both need mockup approval before implementation.

**Risks / open questions:**
- The `beforeunload` prompt in T-008 does not work reliably in all browsers inside SPAs — consider a simpler "You have unsaved changes — leave?" Angular `CanDeactivate` guard instead.
- Cross-module call in T-005 adds a new entry to `DevHub.Contracts`; confirm the interface name and registration pattern with the existing `IProjectAuthorizationService` pattern.
