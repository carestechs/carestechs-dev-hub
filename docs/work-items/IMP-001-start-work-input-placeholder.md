# Improvement Proposal: IMP-001 — Per-protocol example payload in Start Work modal

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | IMP-001 |
| **Name** | Per-protocol example payload hint in Start Work modal |
| **Type** | Developer Experience |
| **Status** | Completed |
| **Priority** | Low |
| **Proposed By** | Operator feedback (2026-05-19) — "can we have an example of the input request as a hint/placeholder?" |
| **Date Created** | 2026-05-19 |

---

## 2. Target Area

**Component / Module:** Start Work modal (operator UI).

**Affected Files / Directories:**

- `client/src/app/features/projects/work-items/start-work.modal.ts`
- `client/src/app/features/projects/work-items/start-work.modal.html`
- `client/src/app/features/projects/work-items/start-work.modal.spec.ts`
- `client/src/app/features/projects/project-home.page.ts` (passes the bound executor's protocol into the modal)
- `client/src/app/features/projects/project-home.page.html`

---

## 3. Current State

### How It Works Today

The Start Work modal renders a JSON textarea pre-filled with `'{}'` and the helper text *"Executor-shaped payload. DevHub passes it through unchanged."* (`start-work.modal.html:17-24`, `start-work.modal.ts:35,47`). The modal has no awareness of the executor protocol bound to the project, so operators get the same empty `{}` regardless of whether the project routes to a `devhub`-protocol or `orchestrator`-protocol executor.

### Problems

1. **Discoverability gap.** Operators starting work against an orchestrator-protocol executor don't know what shape the orchestrator expects (e.g. `{ "task": "..." }` vs. some other key). The helper text says "executor-shaped" without showing the shape.
2. **Trial-and-error onboarding.** New operators submit `{}`, get a downstream validation error from the executor (surfaced as a 502 problem detail), then guess. The example lives in the orchestrator repo, not where the operator is working.
3. **Protocol context is already known.** `ExecutorDto.protocol` (`'devhub' | 'orchestrator'`) is fetched and rendered next to the executor name today (T-087), but the modal that needs it most ignores it.

### Evidence

- Operator request on 2026-05-19 (this proposal's trigger).
- T-087 introduced the protocol label in the operator UI (`60f587d feat(T-087): operator UI — protocol picker + executorRunId label`), confirming the protocol is plumbed into the page-level component already.

---

## 4. Desired State

### Target Implementation

The Start Work modal accepts a `protocol: ExecutorProtocol` input from its parent and uses it to choose an example payload that's rendered as a placeholder (and used as the initial textarea value when the modal opens). The example is a static, hard-coded constant per protocol — no schema fetching, no orchestrator changes.

To make the parent able to forward `protocol` deterministically, `ProjectDto` gains a nullable `BoundExecutorProtocol` field, populated on single-project loads (`GetAsync` / `GetBySlugAsync`) via the existing `IExecutorRouter.ResolveAsync(projectId)` cross-module contract. `ListAsync` continues to return `null` for this field — list rows do not open the modal, so the per-row router call is unnecessary and would invite an N+1. The orchestrator is not touched.

### Benefits

1. **Self-serve start.** Operators see a valid example for the bound executor protocol and can edit-in-place rather than authoring from scratch.
2. **Zero cross-repo coordination.** The example is a frontend constant; the orchestrator team is not in the critical path.
3. **Forward-compatible.** When a third protocol is added, the constant table grows by one entry; the call sites don't change.

---

## 5. Trigger and Motivation

**Trigger:** Operator usability feedback on 2026-05-19 while smoke-testing FEAT-010's orchestrator path end-to-end.

**Impact if deferred:** Low. Operators continue to work around the gap via tribal knowledge / the orchestrator repo's README. The cost of deferring grows linearly with the number of new operators onboarded to orchestrator-backed projects.

**Dependencies on this improvement:** None blocking. Loose dependency: FEAT-010 (orchestrator protocol exists), T-087 (protocol field surfaced in the page that hosts the modal).

---

## 6. Affected Entities and Components

| Entity / Component | What Changes | Spec Reference |
|--------------------|-------------|----------------|
| `StartWorkModal` | New `protocol` input; new internal `examplePayloadFor(protocol)` helper; `inputJson` initial value sourced from helper instead of literal `'{}'`; `<textarea placeholder>` bound to the same example. | `docs/ui-specification.md` § Start Work modal |
| `ProjectHomePage` (`project-home.page.ts/html`) | Passes the bound executor's `protocol` to `<start-work-modal [protocol]="...">`. | `docs/ui-specification.md` § Project Home |
| `StartWorkItemRequest` (DTO) | No change. The shape on the wire is unchanged; DevHub still passes the payload through to the executor. | `docs/api-spec.md` § Work Items — Start |
| `ProjectDto` | New nullable `BoundExecutorProtocol: 'devhub' \| 'orchestrator' \| null` field. Populated on `GetAsync` / `GetBySlugAsync` via `IExecutorRouter.ResolveAsync(projectId)`. `null` on list responses (intentional — list doesn't open the modal). | `docs/api-spec.md` § ProjectDto + `docs/data-model.md` § Project |
| `ProjectService` (Workspace module) | `LoadAsync` calls the already-injected `IExecutorRouter` to resolve the bound descriptor and projects `Protocol` onto the DTO. | `src/DevHub.Modules.Workspace/Services/ProjectService.cs` |
| `DevHub.Modules.WorkItems`, `ExecutorRegistry`, `OrchestratorExecutorClient` | **No change.** | — |
| `carestechs-agent-orchestrator` | **No change.** Examples are derived from the orchestrator's known intake contract and frozen client-side. | — |

---

## 7. Risk Assessment

### Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Hard-coded example drifts from the orchestrator's real intake schema as the orchestrator evolves | Medium | Low | Co-locate the constants with a short comment pointing to the orchestrator schema file; revisit when the orchestrator's `/api/v1/runs` contract changes (already a coordination point). The wire DTO is unaffected, so worst case is a misleading hint — not a runtime failure. |
| Operator confuses the placeholder for required content and submits the literal example unchanged | Low | Low | The example is a *placeholder* for an empty field and the *initial value* on open; either way it's still valid JSON and routes to the executor, which will return a 502/validation error for clearly-fake task strings as it does today. |
| Pre-filling the textarea with a non-empty value masks a real "I forgot to fill this in" mistake | Low | Low | Keep the example minimal (one field) and obviously placeholder-like (e.g. `"Describe the task to run"`). Operators must edit it before submitting anything meaningful. |

### Rollback Strategy

Pure frontend change behind no flag. Rollback = revert the modal/page commits. No data migration, no executor coordination, no contract change.

---

## 8. Constraints

- **Wire DTO for Start Work is unchanged.** `StartWorkItemRequest` and the façade `POST /api/projects/{id}/work-items` behavior stay identical.
- **One additive backend field only.** `ProjectDto.BoundExecutorProtocol` is the only backend change permitted; populated via the existing `IExecutorRouter` contract. No new endpoints, no new tables, no migration, no schema-fetching path.
- **No orchestrator change.** Examples are derived from the orchestrator's documented intake contract and frozen client-side.
- **No new dependency.** No JSON-schema libraries, no AJV, no Monaco. Plain textarea with a `placeholder` attribute.
- **Standalone-component / Signals / Tailwind-only conventions** per `CLAUDE.md`.
- **Out of scope:** dynamic / executor-provided schema fetching (deferred to a future IMP if/when operator demand warrants it — see Section 11).

---

## 9. Success Criteria

- `GET /api/projects/{id}` and `GET /api/projects/by-slug/{slug}` return a `boundExecutorProtocol` field equal to the bound executor's protocol (or `null` when the project has no active binding).
- `GET /api/projects` (list) returns `boundExecutorProtocol: null` for every row — confirmed by spec, not accidental.
- Opening Start Work on a project bound to an `orchestrator`-protocol executor shows an example payload matching the orchestrator's `RunCreateRequest` intake shape (e.g. `{ "task": "Describe the task to run" }`) as both the placeholder and the initial textarea value.
- Opening Start Work on a project bound to a `devhub`-protocol executor shows the prior behavior (`{}` placeholder / initial value) — explicit, not accidental.
- Opening Start Work on a project with no binding falls back to the orchestrator example (safe default; the start request will surface a 502 from the façade's pre-check).
- Submitting without editing the example still produces a valid `StartWorkItemRequest` (JSON parses; backend forwards it; executor decides whether to accept).
- Component spec (`start-work.modal.spec.ts`) covers: (a) protocol input drives the initial value, (b) placeholder reflects the selected protocol, (c) reset-on-open uses the protocol-specific example.
- Backend spec coverage: at least one test per single-project load path (`Get` / `GetBySlug`) asserting `BoundExecutorProtocol` is filled from `IExecutorRouter.ResolveAsync`, plus one list-path test asserting it stays `null`.

---

## 10. Current Test Coverage

| Area | Coverage | Notes |
|------|----------|-------|
| `start-work.modal.spec.ts` | Good | Existing specs cover open/close, validation, JSON parse error, submit emission. |
| `project-home.page.spec.ts` | Good | Existing specs cover the Start button wiring; will need one new assertion that `protocol` is forwarded to the modal. |
| `ProjectService` tests | Good | Existing tests cover Get/GetBySlug/List. Will need new assertions: `BoundExecutorProtocol` populated on single-project loads via `IExecutorRouter`; `null` on list rows. Use the existing router-fake/mocking pattern from FEAT-004 / FEAT-008 tests. |

---

## 11. Traceability

| Reference | Link |
|-----------|------|
| **Triggered By** | Operator feedback during FEAT-010 smoke (2026-05-19). |
| **Stakeholder Alignment** | "DevHub is the single front door humans use to start work" (`docs/stakeholder-definition.md`) — reduces friction at the most common entry point. |
| **Architecture Reference** | `docs/ARCHITECTURE.md` § Frontend / Operator UI; modal is a feature-route component under `client/src/app/features/projects/work-items/`. |
| **Related Work Items** | FEAT-010 (orchestrator protocol exists), T-087 (protocol surfaced in operator UI). |
| **Blocked Features** | None. |
| **Follow-on (not in scope)** | A future IMP could replace the hard-coded examples with an executor-served input schema/example endpoint. Out of scope here because (a) no orchestrator endpoint exists today and (b) operator demand is currently a single ask. |

> **Scope expansion note (2026-05-19):** The first draft of this IMP held "no backend change" as a hard constraint. On generating tasks it became clear that the parent page cannot pass `[protocol]` to the modal without resolving the bound executor — and the cross-module contract for that (`IExecutorRouter`) is already injected into `ProjectService`. The IMP was widened to permit a single additive field on `ProjectDto`; nothing else in the backend moves.

---

## 12. Usage Notes for AI Task Generation

When generating tasks from this proposal:

1. **Phase 0 (safety net)** is trivial — existing specs already cover the modal; one new spec assertion is enough before any implementation task.
2. **One implementation task** is sufficient: add `protocol` input + example constants + placeholder + initial-value wiring + parent forwarding.
3. **One verification task** to confirm success criteria in Section 9.
4. **Do not** generate tasks that touch the backend, the orchestrator, or the wire DTO — constraint in Section 8.
5. **Do not** generate tasks that introduce schema-fetching, AJV, JSON-schema validation, or a code editor component — explicitly out of scope (see Section 11 follow-on note).
6. Cross-reference IMP-001 in commit messages and task IDs.
