# IMP-001 Task Breakdown — Per-protocol example payload in Start Work modal

> Generated from `docs/work-items/IMP-001-start-work-input-placeholder.md` using `.ai-framework/prompts/refactor-tasks.md`. **3 tasks**: one small backend addition, one frontend change, one verification. No orchestrator change.

## Scope choices locked in before generation

- **One additive backend field, nothing else.** `ProjectDto` gains a nullable `BoundExecutorProtocol`. Populated on single-project loads via `IExecutorRouter.ResolveAsync(projectId)`, which is already injected into `ProjectService`. List rows leave it `null` — deliberately, to avoid an N+1 router call across every paged list response. See IMP-001 §8.
- **No new endpoint, no migration, no new table, no DTO renames.** The wire `StartWorkItemRequest` is untouched; the façade `POST /api/projects/{id}/work-items` is untouched.
- **No orchestrator change.** Examples are hard-coded client-side constants derived from `carestechs-agent-orchestrator/src/app/modules/ai/schemas.py § RunCreateRequest`.
- **Modal-side default when `protocol` is null.** Even with the backend addition, projects with no active binding return `boundExecutorProtocol: null`. The modal falls back to the orchestrator example in that case — safe default; the façade's existing pre-checks still surface a 502 if Start is attempted with no binding.
- **No schema fetching, no AJV, no JSON-schema dependency, no Monaco editor.** Plain `<textarea placeholder>` with a static-constant initial value. Per IMP-001 §8.
- **Test coverage (Phase 0) folds into each task.** IMP-001 §10 notes existing coverage is good; new assertions land inside the same task as the implementation rather than as a standalone safety-net task.

---

## Foundation

### T-090: Expose `BoundExecutorProtocol` on `ProjectDto`

**Type:** Backend · **Workflow:** standard · **Complexity:** S · **Dependencies:** None

**Description:**
Add a nullable `BoundExecutorProtocol` field to `ProjectDto`. Populate it on single-project loads (`GetAsync`, `GetBySlugAsync`, `CreateAsync`, `UpdateAsync` — anything that returns a single project DTO) via the already-injected `IExecutorRouter.ResolveAsync(projectId)`. List responses pass `null`. Update `docs/api-spec.md` and `docs/data-model.md` with changelog entries.

**Rationale:**
The frontend modal needs the bound executor's protocol to choose the right example payload. The cross-module contract for that resolution (`IExecutorRouter`) already exists and is already wired into `ProjectService` (`src/DevHub.Modules.Workspace/Services/ProjectService.cs:27`). This task is the minimum surface needed; the per-protocol UX win is delivered in T-091.

**Acceptance Criteria:**
- [ ] `ProjectDto` (positional record at `src/DevHub.Modules.Workspace/DTOs/ProjectDtos.cs:7`) gains a new trailing positional parameter `string? BoundExecutorProtocol`. Place it after `CreatedAt` to minimize ctor-arg-reorder churn in tests.
- [ ] `ProjectService.LoadAsync` (`ProjectService.cs:233`) resolves `await router.ResolveAsync(row.Id, ct)` after the DB read and projects `descriptor?.Protocol` onto the DTO. Catch nothing — if the router throws, that's a bug, surface it.
- [ ] All single-project-returning methods (`GetAsync`, `GetBySlugAsync`, `CreateAsync`, `UpdateAsync`) automatically benefit because they all funnel through `LoadAsync` — verify by code reading. (`CreateAsync` returns via a final `LoadAsync`; confirm same for `UpdateAsync`.)
- [ ] `ProjectService.ListAsync` (`ProjectService.cs:30`) constructs `ProjectDto` rows with `BoundExecutorProtocol: null`. No call to `IExecutorRouter` from the list path.
- [ ] `docs/api-spec.md § ProjectDto` updated: new `boundExecutorProtocol` field row documenting type `'devhub' | 'orchestrator' | null`, semantics, and the list-vs-single-load contract. Changelog row appended at the bottom of the file.
- [ ] `docs/data-model.md § Project` updated: prose note that "Bound executor protocol is not stored on Project; it is projected at read time from the active `ExecutorBinding` for the project's `projectType`." Changelog row appended.
- [ ] `tests/DevHub.Modules.Workspace.Tests` adds:
  - One test asserting `GetAsync` / `GetBySlugAsync` returns `BoundExecutorProtocol == "orchestrator"` when the router fake returns an `orchestrator`-protocol descriptor for the project id.
  - One test asserting the same path returns `BoundExecutorProtocol == null` when the router returns `null` (no binding).
  - One test asserting `ListAsync` returns `BoundExecutorProtocol == null` on every row regardless of the router fake's setup.
- [ ] `dotnet test` green. Existing tests adjust to the new ctor signature (positional record); the expected churn is mechanical (named or `with`-expression projections in test fixtures need the new arg). Verify the count delta is "+3 new tests, 0 deletions."

**Files to Modify/Create:**
- Modify: `src/DevHub.Modules.Workspace/DTOs/ProjectDtos.cs` (add `BoundExecutorProtocol`)
- Modify: `src/DevHub.Modules.Workspace/Services/ProjectService.cs` (`LoadAsync` + `ListAsync` projections)
- Modify: `tests/DevHub.Modules.Workspace.Tests/Services/ProjectServiceTests.cs` (or wherever the existing Get/List tests live — confirm path during planning)
- Modify: `docs/api-spec.md` (ProjectDto section + changelog)
- Modify: `docs/data-model.md` (Project section + changelog)

**Technical Notes:**
- `IExecutorRouter.ResolveAsync` already returns `ExecutorRegistrationDescriptor?` (nullable). The descriptor has a `Protocol` field (string, defaulted to `"devhub"` for legacy registrations — see T-084 / FEAT-010). Project to DTO with `descriptor?.Protocol`.
- `LoadAsync` is currently a single DB round-trip via a projection. The new router call is a second await (and may itself do a DB read against `ExecutorRegistryDbContext`). Acceptable — single-project loads are not on a hot path, and this is intentionally not added to list.
- Per `CLAUDE.md` modular-monolith rule: do **not** add a navigation property from `Project` to `ExecutorBinding`. The cross-module call through `IExecutorRouter` is the only correct mechanism.
- Do **not** add new audit entries for this read-only projection.

---

### T-091: Per-protocol example payload in `StartWorkModal` (with parent wiring)

**Type:** Frontend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-090

**Description:**
Add a `protocol` input to `StartWorkModal`, source the textarea's initial value and `placeholder` from a per-protocol example constants table, and wire `ProjectHomePage` to forward `project.boundExecutorProtocol` to the modal. Update the frontend `ProjectDto` type. When the page passes `null` (no binding), the modal falls back to the orchestrator example.

**Rationale:**
Delivers IMP-001's operator UX win (§3 problems 1–3). Now deterministic — the example matches the bound executor's protocol, not a hard-coded guess. Frontend-only changes (the corresponding backend addition lives in T-090).

**Acceptance Criteria:**
- [ ] `ProjectDto` in `client/src/app/core/api/workspace.types.ts` gains a `boundExecutorProtocol: ExecutorProtocol | null` field. Import `ExecutorProtocol` from `client/src/app/core/api/executor-registry.types.ts`.
- [ ] `StartWorkModal` accepts a new `protocol = input<ExecutorProtocol | null>(null)` input.
- [ ] A module-private constant `EXAMPLE_PAYLOADS: Record<ExecutorProtocol, string>` exists in `start-work.modal.ts`:
  - `'devhub'` → `'{}'`
  - `'orchestrator'` → multi-line JSON string with a single `task` field, value `"Describe the task to run"`. One-line comment pointing to `carestechs-agent-orchestrator/src/app/modules/ai/schemas.py § RunCreateRequest`.
- [ ] A `computed()` `exampleJson` returns `EXAMPLE_PAYLOADS[this.protocol() ?? 'orchestrator']`. The reset-on-open `effect()` (`start-work.modal.ts:44-51`) uses `exampleJson()` instead of the literal `'{}'`.
- [ ] The textarea binds `[placeholder]="exampleJson()"`.
- [ ] `project-home.page.html` updates `<start-work-modal ...>` to bind `[protocol]="project()?.boundExecutorProtocol ?? null"`.
- [ ] `project-home.page.ts` imports nothing new for runtime — the protocol comes off the existing `project()` signal. (Type-only `ExecutorProtocol` import if needed for a helper; otherwise leave untouched.)
- [ ] `start-work.modal.spec.ts` adds:
  - Spec: initial textarea value is the orchestrator example when no `protocol` is set.
  - Spec: initial textarea value is `'{}'` when `protocol="devhub"`.
  - Spec: `placeholder` attribute mirrors the initial value (both protocols).
  - Spec: submitting the unedited orchestrator example emits a valid `StartWorkItemRequest` whose `input` deep-equals `{ task: 'Describe the task to run' }`.
- [ ] `project-home.page.spec.ts` adds one assertion that the rendered `<start-work-modal>` receives the `boundExecutorProtocol` from the loaded project (use a fake `ProjectDto` with `boundExecutorProtocol: 'orchestrator'` and inspect the modal directive's input or the rendered component's `protocol()` value).
- [ ] `cd client && ng test` green. `dotnet test` unaffected.

**Files to Modify/Create:**
- Modify: `client/src/app/core/api/workspace.types.ts` (add `boundExecutorProtocol` to `ProjectDto`)
- Modify: `client/src/app/features/projects/work-items/start-work.modal.ts`
- Modify: `client/src/app/features/projects/work-items/start-work.modal.html`
- Modify: `client/src/app/features/projects/work-items/start-work.modal.spec.ts`
- Modify: `client/src/app/features/projects/project-home.page.html` (one new attribute binding)
- Modify: `client/src/app/features/projects/project-home.page.spec.ts`

**Technical Notes:**
- Keep the per-protocol constants and the default selection co-located in `start-work.modal.ts`. No new types file.
- Bind `[placeholder]` as a property binding, not an attribute interpolation — keeps the reactive `computed()` dependency live.
- The modal's `protocol` input is nullable (not optional) so the parent can pass `null` explicitly when the project has no binding. The default-resolution happens inside the modal, not the page.
- Test workspace fixtures: search for an existing `ProjectDto` test fixture builder; if one exists, extend it to set a sensible default for `boundExecutorProtocol` (likely `null` so existing tests don't change in behavior).

---

## Verification

### T-092: Verification & docs sweep

**Type:** Testing · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-091

**Description:**
End-to-end verification of IMP-001's success criteria. `docs/ui-specification.md` updated to reflect the new modal behavior and the `boundExecutorProtocol` ProjectDto field. IMP-001 status flipped to `Completed`.

**Rationale:**
IMP-001 §9 enumerates eight success criteria spanning backend response shape, frontend behavior, and operator UX. A single verification pass closes them out. Per `CLAUDE.md` Documentation Maintenance Discipline, the UI/API doc updates land before the work item closes.

**Acceptance Criteria:**
- [ ] Manual smoke: `GET /api/projects/by-slug/{slug}` returns `boundExecutorProtocol: "orchestrator"` for a project bound to an orchestrator-protocol executor; `boundExecutorProtocol: "devhub"` for a `devhub` binding (e.g. FakeExecutor); `null` for an unbound project. Use `curl` or the browser devtools network tab.
- [ ] Manual smoke: `GET /api/projects` (list) returns `boundExecutorProtocol: null` on every row.
- [ ] Manual smoke: open the Start Work modal on each of the three binding states above and confirm the example payload and placeholder match expectations.
- [ ] Manual smoke: submitting an unedited orchestrator example produces a valid `POST /api/projects/{pid}/work-items` (verify in devtools); the orchestrator's validation error (if any) surfaces in the existing `serverError` banner.
- [ ] `docs/ui-specification.md` § Start Work modal (add the section if missing) documents the per-protocol example payload behavior. Changelog row appended.
- [ ] `docs/work-items/IMP-001-start-work-input-placeholder.md` Status field set to `Completed`.
- [ ] `dotnet test` green; `cd client && ng test` green.

**Files to Modify/Create:**
- Modify: `docs/ui-specification.md` (§ Start Work modal + changelog row)
- Modify: `docs/work-items/IMP-001-start-work-input-placeholder.md` (Status → Completed)

---

## Summary

- **Total tasks:** 3 (T-090 backend, T-091 frontend + wiring, T-092 verification).
- **Critical path:** T-090 → T-091 → T-092. No parallelism.
- **Risk assessment:** Low. T-090 is a single additive field with no migration; existing `IExecutorRouter` usage in `ProjectService` means we are not standing up a new dependency. T-091 is a small UI change behind no flag. T-092 is verification only.
- **Recommended review points:** After T-090, confirm the test fixtures (positional `ProjectDto` ctor) compile cleanly and `dotnet test` remains green. After T-091, confirm the binding state matrix (orchestrator / devhub / null) renders the right example.
- **Rollback strategy:** Per IMP-001 §7 — pure git revert. The `boundExecutorProtocol` field is additive and nullable, so a frontend revert without a backend revert continues to work (the FE ignores the field). A backend revert without a frontend revert produces `undefined` on the FE, which falls back to the orchestrator example — also acceptable.
- **Out of scope (do not generate tasks for):** schema-fetching, AJV, JSON-schema validation, code editor component, new endpoints, migrations, navigation properties between Workspace and ExecutorRegistry, orchestrator-side schema endpoints.
