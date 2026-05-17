# FEAT-008 Task Breakdown — Code-Source Binding (project repo + optional work branch)

> Generated from `docs/work-items/FEAT-008-code-source-binding.md` using `.ai-framework/prompts/feature-tasks.md`. 9 tasks across Database, Backend, Frontend, Testing.

## Scope choices locked in before generation

- **`Project.Repo` and `Project.DefaultBranch` are nullable in v1.** Making them required would break the create modal that just shipped (PR #45) and force backfill of every existing test fixture. The strictness lives in the orchestrator's deprecation-flag flip, not in DevHub's schema.
- **`WorkItem.WorkBranch` opens an Update path that didn't exist before.** Today `StartAsync` is the only WorkItem mutation. This FEAT introduces a small `UpdateAsync` + `PATCH /api/projects/{pid}/work-items/{wid}` endpoint scoped to one field initially — but the slot is reusable for later FEATs (e.g., FEAT-010 PR linkage).
- **Forward payload is additive.** `intake.codeSource` is added under a new top-level `intake` envelope alongside the existing `{ input, correlationMarker }` root fields. The existing FakeExecutor `/work-items` contract continues to accept payloads with no `intake` envelope; that path is what AC-6 / AC-7 verify.
- **Field names match the orchestrator exactly** (`intake.codeSource`, `repo`, `baseBranch`, `workBranch`) — no DevHub-side renaming. Omitted (not `null`) when not set.
- **Validation parity, not validation duplication.** A small shared `CodeSourceValidator` static class lives in `DevHub.Contracts` and is invoked from both Workspace and WorkItems services. Single source of rules.
- **The Project create modal already exists** (PR #45) — T-061 extends it rather than building a new screen, so workflow stays `standard` per the prompt's CRUD-screen exception.

---

## Foundation

### T-055: EF migrations — Project (Repo, DefaultBranch) + WorkItem (WorkBranch)

**Type:** Database · **Workflow:** standard · **Complexity:** S · **Dependencies:** None

**Description:**
Add `Repo` (string?, max 140) and `DefaultBranch` (string?, max 200) to `Project`. Add `WorkBranch` (string?, max 200) to `WorkItem`. Generate EF migrations in both modules. Update the entity classes and `OnModelCreating` configurations. All three columns nullable; default `NULL`.

**Rationale:**
AC-1. Every other task is gated on the columns existing. Foundation layer per the framework's task-grouping rule.

**Acceptance Criteria:**
- [ ] `Project` entity has `string? Repo` and `string? DefaultBranch` with `MaxLength(140)` and `MaxLength(200)` respectively.
- [ ] `WorkItem` entity has `string? WorkBranch` with `MaxLength(200)`.
- [ ] EF migrations generated under `src/DevHub.Modules.Workspace/Migrations/` and `src/DevHub.Modules.WorkItems/Migrations/` add nullable text columns `repo`, `default_branch`, `work_branch` (snake_case via the existing EF naming convention).
- [ ] `dotnet ef database update` against both modules applies cleanly to a database created from the previous migrations. Existing rows survive with `NULL` in the new columns.
- [ ] `dotnet test` passes (Testcontainers re-bootstraps the schema per run; the new columns are visible end-to-end).
- [ ] `docs/data-model.md` Project + WorkItem sections updated + changelog entry.

**Files to Modify/Create:**
- Modify: `src/DevHub.Modules.Workspace/Entities/Project.cs`
- Modify: `src/DevHub.Modules.WorkItems/Entities/WorkItem.cs`
- Modify: `src/DevHub.Modules.Workspace/Persistence/WorkspaceDbContext.cs` (column maxlength config)
- Modify: `src/DevHub.Modules.WorkItems/Persistence/WorkItemsDbContext.cs`
- Create: `src/DevHub.Modules.Workspace/Migrations/<timestamp>_AddProjectRepoAndDefaultBranch.cs` (+ designer)
- Create: `src/DevHub.Modules.WorkItems/Migrations/<timestamp>_AddWorkItemWorkBranch.cs` (+ designer)
- Modify: `docs/data-model.md` (Project + WorkItem entity definitions, changelog row)

**Technical Notes:**
Each module owns its own DbContext per the modular monolith rule — two separate migrations, two separate `dotnet ef migrations add` invocations against the two project paths. No cross-module FK, no shared DbContext.

---

### T-056: CodeSourceValidator utility + unit tests

**Type:** Backend · **Workflow:** standard · **Complexity:** S · **Dependencies:** None (parallel-safe with T-055)

**Description:**
Add a static `CodeSourceValidator` in `DevHub.Contracts/Validation/` with two methods: `ValidateRepo(string repo)` and `ValidateBranch(string branch)`. Each throws a `ValidationException` (existing `DevHub.Contracts.ApplicationErrors`) on failure with a clear rule-name suffix in the message. Unit tests cover every reject + accept case enumerated in the brief.

**Rationale:**
AC-3, AC-4. Boundary-parity with the orchestrator's `intake.codeSource` schema means we need one validator used by Workspace (project repo/branch) AND WorkItems (work branch). Centralizing it ensures the rules stay in lockstep when the orchestrator tightens them.

**Acceptance Criteria:**
- [ ] `ValidateRepo` rejects: empty, whitespace-only, `"https://github.com/foo/bar"`, `"foo/bar.git"`, `"foo"` (no slash), `"foo/bar/baz"` (two slashes), `"foo bar/baz"` (whitespace), `"/foo/bar"` (leading slash). Accepts: `"acme/widgets"`, `"my-org/repo.with.dots"`, `"a/b"`.
- [ ] `ValidateBranch` rejects: empty, whitespace anywhere, leading `/`, contains `..`, ASCII control characters (`\x00-\x1F`, `\x7F`). Accepts: `"main"`, `"feat/imp-042"`, `"release/v1.2.3"`.
- [ ] `ValidationException` messages include both the rejected value (truncated to 200 chars) and the rule name (e.g., `"repo: must match 'owner/name' shape"`).
- [ ] xUnit suite `tests/DevHub.Contracts.Tests/Validation/CodeSourceValidatorTests.cs` exercises every accept + reject from the brief, ≥ 20 cases total.

**Files to Modify/Create:**
- Create: `src/DevHub.Contracts/Validation/CodeSourceValidator.cs`
- Create: `tests/DevHub.Contracts.Tests/Validation/CodeSourceValidatorTests.cs`

**Technical Notes:**
Use a precompiled `Regex` for the repo shape (`^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$`). For branches, walk the string char-by-char rather than chaining `Contains` — it's both clearer and faster for the ASCII-control-char check. No external deps.

---

## Backend

### T-057: Project DTOs + service + audit for Repo / DefaultBranch

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-055, T-056

**Description:**
Thread `Repo` + `DefaultBranch` through Project creation, retrieval, and update. Validate via `CodeSourceValidator` before any DB write. Audit entries on Create/Update include before/after values in `details` when these fields change. No change to the controller surface other than the request/response DTO shapes.

**Rationale:**
AC-2, AC-3, AC-10 (Project half). Where Project becomes the source of truth for the repo coordinates.

**Acceptance Criteria:**
- [ ] `ProjectDto`, `CreateProjectRequest`, `UpdateProjectRequest` carry `Repo` + `DefaultBranch` (both optional / nullable). System.Text.Json serializes them as `repo` and `defaultBranch`.
- [ ] `ProjectService.CreateAsync` invokes `CodeSourceValidator.ValidateRepo(req.Repo)` and `ValidateBranch(req.DefaultBranch)` when those fields are set. Validation failure throws `ValidationException` → `400 application/problem+json` via the global exception handler. No project row written.
- [ ] `ProjectService.UpdateAsync` same, and writes audit `details` containing `repoBefore`, `repoAfter`, `defaultBranchBefore`, `defaultBranchAfter` only for the fields that actually changed.
- [ ] `GET /api/projects`, `GET /api/projects/{id}`, `GET /api/projects/by-slug/{slug}` return the new fields in the envelope.
- [ ] Existing `WorkspaceModuleAcceptanceTests` still pass (deny paths + auth checks unchanged).
- [ ] `docs/api-spec.md` Project section updated + changelog row.

**Files to Modify/Create:**
- Modify: `src/DevHub.Modules.Workspace/DTOs/ProjectDtos.cs` (ProjectDto, CreateProjectRequest, UpdateProjectRequest)
- Modify: `src/DevHub.Modules.Workspace/Services/ProjectService.cs` (Create + Update + LoadAsync mapping)
- Modify: `docs/api-spec.md`

**Technical Notes:**
`LoadAsync` is the existing projection helper that maps `Project` → `ProjectDto`. Two new columns on the projection. The validator throws `ValidationException` rather than returning a result — that's the convention everywhere else in this service layer.

The audit `details` dictionary already supports arbitrary string→object pairs; just add the four optional keys.

---

### T-058: WorkItem — `WorkBranch` on Start + new Update endpoint

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-055, T-056

**Description:**
Extend `StartWorkItemRequest` with optional `WorkBranch` (validated via `CodeSourceValidator.ValidateBranch`). Add a new `UpdateWorkItemRequest` DTO, `IWorkItemsService.UpdateAsync(projectId, workItemId, request, actingMemberId, ct)`, controller endpoint `PATCH /api/projects/{pid}/work-items/{wid}`, and audit entry. v1 scope: the only field updatable is `WorkBranch`; the slot is intentionally there for future FEATs.

**Rationale:**
AC-2 (WorkItem half), AC-9, AC-10 (WorkItem half). Today WorkItems have no Update endpoint. This task opens the update path scoped to one field.

**Acceptance Criteria:**
- [ ] `StartWorkItemRequest` gains `string? WorkBranch` with model-level `MaxLength(200)` and runtime validation via `CodeSourceValidator.ValidateBranch` (called from `WorkItemsService.StartAsync` before any executor call).
- [ ] `WorkItemDto` and `WorkItemSummaryDto` carry `WorkBranch`.
- [ ] New `UpdateWorkItemRequest { string? WorkBranch }` DTO; sealed record with `init` setter.
- [ ] New `PATCH /api/projects/{pid}/work-items/{wid}` endpoint on `WorkItemsController`. Auth is the same as other WorkItem mutations (`workitem:update` via project-membership / operator gate; reuse the existing authz helper).
- [ ] `WorkItemsService.UpdateAsync` validates the branch, writes the change inside a transaction, and emits a `workitem:update` audit entry with `details = { workBranchBefore, workBranchAfter }`.
- [ ] Existing `WorkItemsModuleAcceptanceTests` still green.
- [ ] `docs/api-spec.md` WorkItem section + changelog row.

**Files to Modify/Create:**
- Modify: `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs`
- Modify: `src/DevHub.Modules.WorkItems/Services/IWorkItemsService.cs`
- Modify: `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs`
- Modify: `src/DevHub.Modules.WorkItems/Controllers/WorkItemsController.cs`
- Modify: `docs/api-spec.md`

**Technical Notes:**
The PATCH endpoint should return `200 OK` with the updated `WorkItemDto` (matches the Workspace module's Update convention). No executor call on Update — the work branch is forwarded *only* on Start (T-059). Editing after start affects nothing the executor sees today; the value is stored for display + for future re-issues.

Authorization key (`workitem:update`) is new — register it in the project authz service. Operator OR project member with the role bound by the executor's `update` checkpoint contract. v1: keep it simple — operator-only, document the choice in the audit reason.

---

### T-059: `WorkItemsService.StartAsync` builds and forwards `intake.codeSource`

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-057, T-058

**Description:**
On `StartAsync`, load the project's `Repo` + `DefaultBranch` and combine with the work item's `WorkBranch` to build an `intake.codeSource` object. Pass it through to `ExecutorHttpClient.StartAsync`, which adds it to the JSON body under a new top-level `intake` envelope when set. Omit (not `null`) any subfield that's unset.

**Rationale:**
AC-5, AC-6, AC-7. The whole point of the FEAT.

**Acceptance Criteria:**
- [ ] `ExecutorHttpClient.StartAsync` signature gains a `CodeSourcePayload? codeSource` parameter. When non-null, the JSON body becomes `{ input, correlationMarker, intake: { codeSource: { repo, baseBranch, workBranch? } } }`. When null, the body stays `{ input, correlationMarker }` byte-for-byte unchanged from today.
- [ ] `CodeSourcePayload` is a `sealed record` in `DevHub.Contracts/Executors/`. JSON property names: `repo`, `baseBranch`, `workBranch`. `workBranch` is omitted (via `JsonIgnoreCondition.WhenWritingNull`) when null.
- [ ] `WorkItemsService.StartAsync` constructs the payload from `project.Repo`, `project.DefaultBranch`, and `request.WorkBranch`. When `project.Repo` is null, the whole `codeSource` is omitted (the orchestrator's deprecation path).
- [ ] Logging: when `codeSource` is omitted on start, log INFO with `codeSourceMissing=true`, project id, work item id — grep target for "callers still on the old contract".
- [ ] No change to the executor's `/work-items/{marker}/...` calls (fetch, signal, stream) — `codeSource` is start-only.

**Files to Modify/Create:**
- Create: `src/DevHub.Contracts/Executors/CodeSourcePayload.cs`
- Modify: `src/DevHub.Modules.WorkItems/Services/ExecutorHttpClient.cs`
- Modify: `src/DevHub.Modules.WorkItems/Services/IExecutorHttpClient.cs`
- Modify: `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs`

**Technical Notes:**
Use `JsonSerializerOptions` with `DefaultIgnoreCondition = WhenWritingNull` on the payload object before passing to `JsonContent.Create`. The simplest path is to build an anonymous-object body that conditionally includes the `intake` key — e.g., `req.Content = JsonContent.Create(codeSource is null ? new { input, correlationMarker } : new { input, correlationMarker, intake = new { codeSource } });`. Byte-for-byte identity in the no-`codeSource` case keeps the FakeExecutor and the orchestrator's deprecation path working unchanged.

The brief says "DevHub treats the orchestrator's 400 as 502" — already true via `ExecutorFailureException` → existing problem-detail translation. No code change needed for that path.

---

## Frontend

### T-060: TS types + workspace.service / work-items service methods

**Type:** Frontend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-057, T-058

**Description:**
Mirror the new fields in the Angular `core/api` layer. `ProjectDto`, `CreateProjectRequest`, `UpdateProjectRequest` gain `repo` + `defaultBranch`. `WorkItemDto`, `StartWorkItemRequest`, new `UpdateWorkItemRequest` gain `workBranch`. Add `WorkspaceService.updateProject` (if missing) and `WorkItemsService.updateWorkItem` methods.

**Rationale:**
Wire-level parity with the backend so the UI tasks have the types and methods they need.

**Acceptance Criteria:**
- [ ] `client/src/app/core/api/workspace.types.ts` extends `ProjectDto`, `CreateProjectRequest`, `UpdateProjectRequest` with optional `repo: string`, `defaultBranch: string`.
- [ ] `client/src/app/core/api/work-items.types.ts` extends `WorkItemDto`, `StartWorkItemRequest`, adds `UpdateWorkItemRequest { workBranch?: string }`.
- [ ] `WorkspaceService` has `updateProject(id, body): Promise<ProjectDto>` (PATCH `/api/projects/{id}`).
- [ ] `WorkItemsService` has `updateWorkItem(projectId, workItemId, body): Promise<WorkItemDto>` (PATCH `/api/projects/{pid}/work-items/{wid}`).
- [ ] Existing service spec files still pass; new methods covered by minimal request-asserting specs.

**Files to Modify/Create:**
- Modify: `client/src/app/core/api/workspace.types.ts`
- Modify: `client/src/app/core/api/workspace.service.ts`
- Modify: `client/src/app/core/api/work-items.types.ts`
- Modify: `client/src/app/core/api/work-items.service.ts`
- Modify: `client/src/app/core/api/workspace.service.spec.ts`
- Modify: `client/src/app/core/api/work-items.service.spec.ts`

**Technical Notes:**
Follow the exact pattern used by `WorkspaceService.updateTeam` — `patch<Envelope<ProjectDto>>` then unwrap `.data`. No new error handling.

---

### T-061: Project form modal + project detail edit affordance

**Type:** Frontend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-060

**Description:**
Extend `project-form.modal` (the one PR #45 just shipped) with two optional fields: `Repo` (placeholder `owner/name`) and `Default branch` (placeholder `main`). Add a "Code source" inline edit affordance on the project detail page (`project-home.page`) — pencil icon → small inline form with the same two fields. Operator-gated; read-only display for non-operators. Show a soft amber banner on the detail page when `repo` is null: "No repo set on this project — once the orchestrator flips the strict flag, starting work items will fail. Click Edit to set the repo and default branch."

**Rationale:**
AC-8 (operator-only edit), plus the brief's "soft warning banner" requirement.

**Acceptance Criteria:**
- [ ] Create modal shows the two new optional fields under "Project type". Client-side validation mirrors the backend (regex `^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$` for repo, no whitespace / no leading `/` / no `..` for branch). Field-level errors surface inline.
- [ ] `project-home.page` shows `Repo` (linked to `https://github.com/<repo>` in a new tab when set, plain text "(not set)" when null) and `Default branch`.
- [ ] An operator clicking the "Edit" affordance opens an inline form; submit calls `WorkspaceService.updateProject` and refreshes the page state. Non-operators do not see the edit affordance.
- [ ] Soft amber banner appears on the project detail page when `repo` is null. Clears immediately after a successful repo edit.
- [ ] Existing `project-form.modal.spec.ts` + `project-home.page.spec.ts` updated and green.

**Files to Modify/Create:**
- Modify: `client/src/app/features/projects/project-form.modal.ts`
- Modify: `client/src/app/features/projects/project-form.modal.html`
- Modify: `client/src/app/features/projects/project-home.page.ts`
- Modify: `client/src/app/features/projects/project-home.page.html`
- Modify: `client/src/app/features/projects/project-form.modal.spec.ts`
- Modify: `client/src/app/features/projects/project-home.page.spec.ts`

**Technical Notes:**
Per the prompt's mockup-first exception: "standard CRUD screens (list/detail/form) or screens that follow an already-approved mockup pattern" → `standard` workflow. The form is already mocked + approved (PR #45 shipped it); we're adding fields, not redesigning.

The `https://github.com/<repo>` link is constructed in the template — no need for a pipe; `repo` is owner/name, so concatenation is safe.

---

### T-062: WorkItem detail — work branch field + effective branch display

**Type:** Frontend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-060

**Description:**
On `work-item-detail.page`, render the "effective branch" line: `workBranch ?? project.defaultBranch ?? "(not set)"`. Add an inline edit affordance (operator-only) to set `workBranch`. Submit calls `WorkItemsService.updateWorkItem`. After successful save, the page state reflects the new value without a full reload.

**Rationale:**
AC-9. Surfaces the new optional override on the WorkItem detail page; closes the read+write loop.

**Acceptance Criteria:**
- [ ] WorkItem detail page shows a "Branch" row with the effective value computed in the template.
- [ ] Operator sees a pencil icon → inline form (single text field). Submit calls `updateWorkItem({ workBranch: value })`. Cancel restores prior state.
- [ ] Empty submit clears `workBranch` (sends `null` in the PATCH body) — falls back to the project default in the display.
- [ ] Client-side branch validation matches the backend rules (no whitespace, no leading `/`, no `..`).
- [ ] `docs/ui-specification.md` updated (WorkItem detail screen) + changelog row.

**Files to Modify/Create:**
- Modify: `client/src/app/features/projects/work-items/work-item-detail.page.ts`
- Modify: `client/src/app/features/projects/work-items/work-item-detail.page.html`
- Modify: `client/src/app/features/projects/work-items/work-item-detail.page.spec.ts`
- Modify: `docs/ui-specification.md`

**Technical Notes:**
The effective-branch computation is template-only; no signal needed. Show "(not set)" italic-muted when both `workBranch` and `project.defaultBranch` are null — same treatment as the project detail page for symmetry.

---

## Testing

### T-063: Integration tests — forward shape + validation + audit

**Type:** Testing · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-057, T-058, T-059

**Description:**
End-to-end fixture tests against the existing Kestrel-hosted `FakeExecutor` covering the forward shape (AC-5, AC-6, AC-7), the boundary-validation deny paths (AC-3, AC-4), and the audit-details invariants (AC-10). One xUnit class `CodeSourceForwardTests` under `tests/DevHub.Modules.WorkItems.Tests/Acceptance/`.

**Rationale:**
The brief's quality bar: "Verified with a fake-executor integration test that asserts the JSON body byte-for-byte on the relevant subtree." Closes the loop on every numbered AC.

**Acceptance Criteria:**
- [ ] `Start_includes_codeSource_when_project_has_repo` — seeds a project with `repo="acme/widgets"`, `defaultBranch="main"`, sets `WorkBranch="feat/abc"` on the request, asserts FakeExecutor received `intake.codeSource = { repo, baseBranch, workBranch }` exactly.
- [ ] `Start_omits_codeSource_block_entirely_when_project_repo_is_null` — body must not contain the `intake` key at all (assert via JSON parsing). Logs assert `codeSourceMissing=true` at INFO level.
- [ ] `Start_omits_workBranch_subfield_when_work_branch_is_null` — `intake.codeSource` is present, but the inner object has no `workBranch` key.
- [ ] `CreateProject_with_invalid_repo_returns_400_no_db_write` — `repo="https://github.com/foo/bar"` → `400`, problem-detail `type` ends with `/validation`, project count unchanged. Denied audit entry written with `details.rule = "repo.shape"` and `details.value` containing the rejected string.
- [ ] `CreateProject_with_invalid_default_branch_returns_400` — same shape for `defaultBranch = "/main"` and `defaultBranch = "feat/..lol"`.
- [ ] `UpdateProject_repo_change_writes_audit_details_with_before_and_after` — operator updates `repo` from `"foo/bar"` to `"foo/baz"`; the audit entry's `details` contains both old and new values.
- [ ] `UpdateWorkItem_workBranch_writes_audit_entry` — operator sets/clears `workBranch`; audit row present with `workitem:update` and the correct before/after.

**Files to Modify/Create:**
- Create: `tests/DevHub.Modules.WorkItems.Tests/Acceptance/CodeSourceForwardTests.cs`
- Create or modify: `tests/DevHub.Modules.Workspace.Tests/Acceptance/CodeSourceProjectTests.cs`

**Technical Notes:**
Reuse the existing `WebApplicationFactory` + Testcontainers Postgres + `FakeExecutor` Kestrel host. The FakeExecutor's `/work-items` handler should record the raw request body (it likely already does); add a test helper to fetch the last-recorded body as a `JsonNode` and assert subtree equality with `JsonNode.DeepEquals`.

For the byte-for-byte AC-6 assertion, parse to `JsonNode` and assert `body["intake"]` is `null` (the key is absent, not the value).

---

## Summary

| Type | Count |
|------|-------|
| Database | 1 (T-055) |
| Backend | 4 (T-056, T-057, T-058, T-059) |
| Frontend | 3 (T-060, T-061, T-062) |
| Testing | 1 (T-063) |
| **Total** | **9** |

| Complexity | Count |
|------------|-------|
| S | 3 (T-055, T-056, T-060) |
| M | 6 (T-057, T-058, T-059, T-061, T-062, T-063) |
| L | 0 |
| XL | 0 |

**Critical path:** T-055 + T-056 → T-057 → T-058 → T-059 → T-063. ~7 tasks of sequential work for the backend deadline-driver. T-061 and T-062 can land in parallel with T-063 once T-060 ships.

**Dependency DAG:**
```
T-055 ─┐
       ├─→ T-057 ─┐
T-056 ─┤         ├─→ T-059 ─→ T-063
       └─→ T-058 ─┘            ↑
                  ├──→ T-060 ─→ T-061
                                T-062
```

**Risks / open questions:**

- **`workitem:update` authorization key is new.** v1 scope = operator-only (documented in T-058). If multi-role updates are later wanted, the authz contract needs an addition. Out of scope for this FEAT.
- **The orchestrator's deprecation flag has no announced flip date.** We assume "a future release" per the IMP-004 notes. T-059's logging at INFO with `codeSourceMissing=true` gives us the grep target to track exposure ahead of the flip.
- **Existing project / work-item fixtures.** Several test fixtures construct `Project` / `WorkItem` entities with positional `init` syntax. The new nullable fields default to `null`, so old fixtures keep compiling — but any fixture that *positionally* constructs the entity needs visiting if the source switches to record-syntax. Watch for this during T-055/T-057.
- **The FakeExecutor's recorded-body assertion helper** may or may not already exist. T-063 may need to add it.
