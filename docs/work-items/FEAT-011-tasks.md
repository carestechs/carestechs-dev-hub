# FEAT-011 Tasks — First-Class Lifecycle-Agent Review Screens

> **Feature Brief:** `docs/work-items/FEAT-011-first-class-lifecycle-screens.md`
> **Task range:** T-097 – T-108
> **Depends on:** FEAT-009 (completed), FEAT-010 (completed), BUG-001 (resolved)

---

## Foundation

### T-097: Investigate orchestrator trace artefact shapes per node type

**Type:** Investigation
**Workflow:** investigation-first
**Complexity:** M
**Dependencies:** None

**Description:**
Read the carestechs-agent-orchestrator source to document the exact trace record shape emitted at each lifecycle-agent node: `confirm_brief`, `confirm_tasks`, `confirm_assignment`, `confirm_plan`, `request_implementation` / `wait_for_implementation`, and `human_review_implementation` / `confirm_review`. For each node, identify: (a) where the agent's output artefact lands in the trace (which `kind`, which field inside `data`), (b) the JSON shape of that artefact, (c) whether the artefact is emitted as a step output, a separate `node_output` record, or attached to `nodeInputs` of the next step. Record findings in a structured table that the downstream tasks can reference.

**Rationale:**
FEAT-011's panels depend on reading structured artefacts from the orchestrator's trace. Without knowing the real shape, we'd build panels against assumptions that could be wrong (the same class of bug BUG-001 uncovered). Investigation first.

**Acceptance Criteria:**
- [ ] For each of the 6 checkpoint node types, the trace record `kind`, the `data` field path to the artefact, and a representative JSON example are documented
- [ ] Findings are recorded in the FEAT-011 brief §6 as an update (replacing the "expected" shapes with verified shapes)
- [ ] Any gaps (nodes that don't emit artefacts, or artefacts that require an additional API call) are flagged with a mitigation plan

**Files to Modify/Create:**
- `docs/work-items/FEAT-011-first-class-lifecycle-screens.md` — update §6 with verified shapes
- Reads (no modifications): `../carestechs-agent-orchestrator/src/app/modules/ai/` (agent definitions, node executors, trace serialization)

**Technical Notes:**
BUG-001 (T-093) already verified the wrapper shape `{kind, data}` and confirmed `operator_signal` for signals and `step` for steps. This task extends that to node-output artefacts specifically. Check `node_output` kind records, `data.output` or `data.result` fields. The orchestrator's `_serialize_trace_record` in `service.py` is the definitive source.

---

### T-098: Enrich `ExecutorStateProjection` — build `steps` array from trace

**Type:** Backend
**Workflow:** standard
**Complexity:** L
**Dependencies:** T-097

**Description:**
Extend `ExecutorStateProjection` to build a `steps` array from the trace records (replacing the current `lastStep` single-object shape). Each entry in the array corresponds to a `kind == "step"` trace record and includes: `key` (checkpoint key derived via `CheckpointDerivation`), `nodeName`, `label` (from a static display-label map), `stepNumber`, `status` (completed / active / pending), and `artefact` (the node's output extracted per T-097 findings). Update `ExecutorStateProjection.Build()` to accept the trace records and produce the enriched shape. Update `OrchestratorExecutorClient.FetchStateAsync()` to pass trace records through.

**Rationale:**
The frontend's `LifecycleTimeline` reads `executorState.steps` but the current projection only produces `lastStep` — the timeline is empty for orchestrator runs. The per-checkpoint panels need artefact data to render meaningful content instead of raw JSON.

**Acceptance Criteria:**
- [ ] `executorState.steps` is an ordered array of step objects with `key`, `nodeName`, `label`, `stepNumber`, `status`, and `artefact` fields
- [ ] Steps are derived from trace records of `kind == "step"`; checkpoint key is derived via `CheckpointDerivation.SignalForNodeName(nodeName)`
- [ ] The currently active step (matching `currentCheckpointKey`) has `status: "active"`; completed steps have `status: "completed"`; future steps (not yet in the trace) are omitted
- [ ] `lastStep` field is removed from the projection (replaced by the array)
- [ ] Artefact extraction follows T-097 findings; null when the trace doesn't include output data for a step
- [ ] Existing `assignments` and `stopReason` fields are preserved unchanged
- [ ] All existing tests in `ExecutorStateProjectionTests` pass (updated for the new shape)

**Files to Modify/Create:**
- `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs` — new `BuildSteps()` method, updated `Build()` signature
- `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs` — pass trace records to projection
- `tests/DevHub.Modules.WorkItems.Tests/Services/Orchestrator/ExecutorStateProjectionTests.cs` — update for new shape
- `tests/DevHub.TestHarness/FakeOrchestrator/` — enrich fake trace records with artefact data if needed

**Technical Notes:**
The display-label map should be a static dictionary in `ExecutorStateProjection` or a new `DisplayLabels` helper: `{"brief-confirmed": "Brief Review", "tasks-confirmed": "Task List", "assignment-confirmed": "Task Assignment", "plan-confirmed": "Plan Review", "implementation-complete": "Implementation Review", "review-completed": "Final Review"}`. Steps without a derivable checkpoint key (unrecognized node names) should still appear in the array with `key: null` and `label` as the raw node name.

---

## Frontend — Per-Checkpoint Panels

### T-099: `BriefReviewPanel` — brief markdown + title/type override

**Type:** Frontend
**Workflow:** mockup-first
**Complexity:** M
**Dependencies:** T-098

**Description:**
Create `BriefReviewPanel` component for the `brief-confirmed` checkpoint. The panel renders the LLM-generated brief as formatted markdown (via `MarkdownRenderer`), shows the proposed title and work-item type in editable form fields, and offers Confirm (sends `{ title, type, notes }` payload) and Reject (sends `{ feedback }` via a textarea) actions. The component receives the artefact from `executorState.steps` and the current checkpoint contract. Generate a mockup first per `.ai-framework/prompts/mockup-generation.md`.

**Rationale:**
Brief review is the first checkpoint in every lifecycle-agent run. Showing the brief as formatted markdown with inline-editable title/type lets the operator validate and adjust the agent's understanding before it generates tasks — currently this is a generic "approve/reject" button with no content.

**Acceptance Criteria:**
- [ ] Renders brief body as markdown via `MarkdownRenderer`
- [ ] Title field is pre-populated from artefact; editable
- [ ] Type field is pre-populated from artefact; editable (dropdown or text input)
- [ ] Confirm button submits signal with `{ title, type, notes }` payload
- [ ] Reject button expands a feedback textarea; submit sends `{ feedback }` with outcome `rejected`
- [ ] Disabled state when `canAct` is false (read-only view of the brief)
- [ ] Graceful fallback when artefact is null or unexpected shape (shows `ArtefactFallback`)

**Files to Modify/Create:**
- `client/src/app/features/projects/work-items/components/brief-review-panel.ts` — new component
- `client/src/app/features/projects/work-items/components/brief-review-panel.html` — template
- `client/src/app/features/projects/work-items/review.page.ts` — import + wire into checkpoint switch
- `client/src/app/features/projects/work-items/review.page.html` — add `@case` for `brief-confirmed`
- `mockups/` — brief review mockup (mockup-first prerequisite)

**Technical Notes:**
Follow the same pattern as `AssignmentConfirmPanel`: standalone component, `input()` for data, `@Output()` EventEmitter for submit. The parent `ReviewPage` handles the signal call. Title/type fields should use `<app-form-field>`.

---

### T-100: `TaskListReviewPanel` — editable task list with reorder/add/remove

**Type:** Frontend
**Workflow:** mockup-first
**Complexity:** L
**Dependencies:** T-098

**Description:**
Create `TaskListReviewPanel` component for the `tasks-confirmed` checkpoint. Renders the agent-generated task list as an editable list where the operator can: reorder (up/down buttons — drag-and-drop is a nice-to-have, not required for v1), remove tasks, add new tasks (inline form), and edit task title/description inline. Confirm sends the curated list as payload; Reject sends feedback. Generate a mockup first.

**Rationale:**
Task list curation is where the operator shapes the work the agent will execute. The current generic bar gives no visibility into the tasks and no way to edit them without knowing the payload schema.

**Acceptance Criteria:**
- [ ] Renders each task as a card/row showing id, title, description
- [ ] Reorder via up/down buttons (or drag-and-drop) updates the list order
- [ ] Remove button deletes a task from the list (with confirmation or undo)
- [ ] "Add task" inline form appends a new task with title + description
- [ ] Inline edit of task title and description (click-to-edit or always-editable)
- [ ] Confirm sends `{ tasks: [{ id, title, description }] }` payload preserving the new order
- [ ] Reject sends `{ feedback }` with outcome `rejected`
- [ ] Disabled state when `canAct` is false
- [ ] Graceful fallback when artefact is null or task list is empty

**Files to Modify/Create:**
- `client/src/app/features/projects/work-items/components/task-list-review-panel.ts` — new component
- `client/src/app/features/projects/work-items/components/task-list-review-panel.html` — template
- `client/src/app/features/projects/work-items/review.page.ts` — import + wire
- `client/src/app/features/projects/work-items/review.page.html` — add `@case` for `tasks-confirmed`
- `mockups/` — task list review mockup

**Technical Notes:**
Task ids in the artefact are agent-generated (e.g. `T-001`). Added tasks need a local id scheme (e.g. `T-NEW-1`) — the orchestrator will normalize on receipt. For v1, up/down buttons are simpler than CDK drag-and-drop and avoid the Angular CDK dependency. Keep the task list in a signal so all edits are reactive.

---

### T-101: `PlanReviewPanel` — plan markdown with task context

**Type:** Frontend
**Workflow:** mockup-first
**Complexity:** S
**Dependencies:** T-098

**Description:**
Create `PlanReviewPanel` component for the `plan-confirmed` checkpoint. Renders the generated implementation plan as markdown (via `MarkdownRenderer`), shows the task context (current task id + title from `executorState`), and offers Confirm/Reject actions. Simpler than the brief panel — no inline editing, just review and approve. Generate a mockup first.

**Rationale:**
Plan review happens once per task in the loop. Showing the plan as formatted markdown with task context gives the operator confidence about what the agent will implement next.

**Acceptance Criteria:**
- [ ] Renders plan body as markdown via `MarkdownRenderer`
- [ ] Shows current task id and title (from `currentTaskId` + assignments map or artefact)
- [ ] Confirm sends signal with outcome `confirmed` and optional `{ notes }` payload
- [ ] Reject expands feedback textarea; sends `{ feedback }` with outcome `rejected`
- [ ] Disabled state when `canAct` is false
- [ ] Graceful fallback when artefact is null

**Files to Modify/Create:**
- `client/src/app/features/projects/work-items/components/plan-review-panel.ts` — new component
- `client/src/app/features/projects/work-items/components/plan-review-panel.html` — template
- `client/src/app/features/projects/work-items/review.page.ts` — import + wire
- `client/src/app/features/projects/work-items/review.page.html` — add `@case` for `plan-confirmed`
- `mockups/` — plan review mockup

**Technical Notes:**
Very similar structure to `BriefReviewPanel` minus the editable fields. Could share a base layout pattern but avoid premature abstraction — keep them as independent components.

---

### T-102: `ImplementationReviewPanel` — summary + branch + diff

**Type:** Frontend
**Workflow:** mockup-first
**Complexity:** M
**Dependencies:** T-098

**Description:**
Create `ImplementationReviewPanel` component for the `implementation-complete` checkpoint. Shows the agent's implementation summary (markdown), the work branch name, and (if available) a diff view via the existing `DiffViewer` component. Actions: Complete (marks implementation done) or Request Changes (with feedback). Generate a mockup first.

**Rationale:**
Implementation review is where the operator verifies the agent's code changes before the agent self-reviews. Showing the branch, summary, and diff inline saves the operator from switching to a terminal or GitHub.

**Acceptance Criteria:**
- [ ] Renders implementation summary as markdown
- [ ] Shows work branch name (from `workItem.workBranch` or artefact)
- [ ] Renders diff via `DiffViewer` when artefact includes diff data
- [ ] "Complete" button sends signal with outcome `complete` and optional `{ notes }` payload
- [ ] "Request Changes" button expands feedback textarea; sends `{ feedback }` with outcome `changes-requested`
- [ ] Disabled state when `canAct` is false
- [ ] Graceful fallback when diff data is absent (shows summary only)

**Files to Modify/Create:**
- `client/src/app/features/projects/work-items/components/implementation-review-panel.ts` — new component
- `client/src/app/features/projects/work-items/components/implementation-review-panel.html` — template
- `client/src/app/features/projects/work-items/review.page.ts` — import + wire
- `client/src/app/features/projects/work-items/review.page.html` — add `@case` for `implementation-complete`
- `mockups/` — implementation review mockup

**Technical Notes:**
The `DiffViewer` component already exists and accepts a `lines` input. The artefact shape for diff data will be determined by T-097. If the orchestrator doesn't emit diffs in the trace, the panel should show the summary + branch and note "view changes in your IDE or GitHub."

---

### T-103: `FinalReviewPanel` — review findings + approve/reject

**Type:** Frontend
**Workflow:** mockup-first
**Complexity:** S
**Dependencies:** T-098

**Description:**
Create `FinalReviewPanel` component for the `review-completed` checkpoint. Shows the agent's self-review findings (what it tested, what passed, concerns remaining) as markdown, displays the task context, and offers Approve (triggers loop-back to next task) or Reject (with feedback for re-implementation). Generate a mockup first.

**Rationale:**
Final review is the gate before a task is marked done and the lifecycle loops back. Showing the agent's review findings gives the operator context for the approve/reject decision.

**Acceptance Criteria:**
- [ ] Renders review findings as markdown via `MarkdownRenderer`
- [ ] Shows current task context (task id + title)
- [ ] "Approve" button sends signal with outcome `approved` and optional `{ notes }` payload
- [ ] "Reject" button expands feedback textarea; sends `{ feedback }` with outcome `rejected`
- [ ] Disabled state when `canAct` is false
- [ ] Graceful fallback when artefact is null

**Files to Modify/Create:**
- `client/src/app/features/projects/work-items/components/final-review-panel.ts` — new component
- `client/src/app/features/projects/work-items/components/final-review-panel.html` — template
- `client/src/app/features/projects/work-items/review.page.ts` — import + wire
- `client/src/app/features/projects/work-items/review.page.html` — add `@case` for `review-completed`
- `mockups/` — final review mockup

**Technical Notes:**
Structurally similar to `PlanReviewPanel`. The key distinction is that Approve here may trigger the loop-back to `assignment-confirmed` for the next task — but that's orchestrator-side behavior, not panel logic. The panel just submits the signal.

---

## Integration

### T-104: Review page — checkpoint switch + panel wiring

**Type:** Frontend
**Workflow:** standard
**Complexity:** M
**Dependencies:** T-099, T-100, T-101, T-102, T-103

**Description:**
Refactor the review page's action section from the current binary `@if (isAssignmentConfirmCheckpoint()) / @else` to a `@switch (workItem().currentCheckpointKey)` that routes to the correct panel. Wire up all six panels (including the existing `AssignmentConfirmPanel`) and fall back to `CheckpointActionBar` for unknown keys. Ensure all panels receive the correct artefact data from `executorState.steps`, the contract, memberships (for assignment), and the `canAct` / `working` state. Update the `ReviewPage` component class to extract the active step's artefact from the steps array.

**Rationale:**
The individual panels (T-099–T-103) are built in isolation; this task wires them into the review page as a unified checkpoint-routing experience.

**Acceptance Criteria:**
- [ ] `@switch` covers all six checkpoint keys + `@default` for `CheckpointActionBar`
- [ ] Each panel receives the correct artefact from `executorState.steps` (matched by `currentCheckpointKey`)
- [ ] `AssignmentConfirmPanel` continues to work identically (no regression)
- [ ] Unknown checkpoint keys render `CheckpointActionBar` with the contract's allowed outcomes
- [ ] All panel submit events route through the existing signal submission flow (`onActionSubmitted` / `onAssignmentSubmitted` or a unified handler)
- [ ] `isAssignmentConfirmCheckpoint` computed can be removed (replaced by the switch)
- [ ] `readExecutorSteps()` helper updated to read from the new `steps` array shape

**Files to Modify/Create:**
- `client/src/app/features/projects/work-items/review.page.ts` — refactor checkpoint routing, artefact extraction
- `client/src/app/features/projects/work-items/review.page.html` — replace `@if/@else` with `@switch`

**Technical Notes:**
Consider adding a unified submit handler that accepts `{ checkpointKey, outcome, payload, taskId? }` so individual panels don't need to know the signal API shape. The parent page already has two handlers (`onActionSubmitted`, `onAssignmentSubmitted`) — unify into one or add per-panel handlers as needed. Keep it simple.

---

## Testing

### T-105: Unit tests — per-checkpoint panels

**Type:** Testing
**Workflow:** standard
**Complexity:** M
**Dependencies:** T-104

**Description:**
Write Angular component tests for all five new panels (`BriefReviewPanel`, `TaskListReviewPanel`, `PlanReviewPanel`, `ImplementationReviewPanel`, `FinalReviewPanel`). Each test suite should cover: rendering with valid artefact data, rendering with null/empty artefact (fallback), submit emits the correct event shape, disabled state when `canAct` is false. `TaskListReviewPanel` additionally tests reorder, add, remove, and edit operations.

**Rationale:**
Each panel has form logic and conditional rendering that should be tested at the component level.

**Acceptance Criteria:**
- [ ] Each panel has a `.spec.ts` file with at least 4 test cases (render, fallback, submit, disabled)
- [ ] `TaskListReviewPanel` has additional tests for reorder, add, remove, edit (at least 4 more)
- [ ] All tests pass with `ng test`
- [ ] Tests use Angular Testing Library patterns consistent with existing `*.spec.ts` files

**Files to Modify/Create:**
- `client/src/app/features/projects/work-items/components/brief-review-panel.spec.ts`
- `client/src/app/features/projects/work-items/components/task-list-review-panel.spec.ts`
- `client/src/app/features/projects/work-items/components/plan-review-panel.spec.ts`
- `client/src/app/features/projects/work-items/components/implementation-review-panel.spec.ts`
- `client/src/app/features/projects/work-items/components/final-review-panel.spec.ts`

**Technical Notes:**
Follow the pattern established in `assignment-confirm-panel.spec.ts`. Use `ComponentFixture` + signal inputs. For `TaskListReviewPanel`, test that the emitted `tasks` array reflects the edited order and content.

---

### T-106: Backend tests — enriched `ExecutorStateProjection`

**Type:** Testing
**Workflow:** standard
**Complexity:** M
**Dependencies:** T-098

**Description:**
Extend `ExecutorStateProjectionTests` to cover the enriched `steps` array projection. Test cases: (a) trace with multiple completed steps produces ordered array with correct keys, labels, and statuses; (b) active step is marked `status: "active"`; (c) artefact data is correctly extracted per node type; (d) empty trace produces empty steps array; (e) unrecognized node names appear with `key: null` and raw node name as label; (f) existing `assignments` and `stopReason` projections still work.

**Rationale:**
The enriched projection is the data foundation for all frontend panels. Regressions here break all six review screens.

**Acceptance Criteria:**
- [ ] At least 6 test cases covering the scenarios above
- [ ] Tests use the real `{kind, data}` trace shape (post-BUG-001 fix)
- [ ] All tests pass with `dotnet test`
- [ ] FakeOrchestrator trace records include artefact data in the enriched shape

**Files to Modify/Create:**
- `tests/DevHub.Modules.WorkItems.Tests/Services/Orchestrator/ExecutorStateProjectionTests.cs` — new/updated tests
- `tests/DevHub.TestHarness/FakeOrchestrator/ScriptedRunResponses.cs` — enrich fake trace data if needed

**Technical Notes:**
BUG-001 (T-094/T-095/T-096) already established the correct trace shape in the test harness. Build on those fixtures.

---

### T-107: Integration test — full lifecycle checkpoint progression

**Type:** Testing
**Workflow:** standard
**Complexity:** L
**Dependencies:** T-104, T-106

**Description:**
Write an integration test that drives a `lifecycle-agent@0.4.0-manual` work item through all six checkpoints against the FakeOrchestrator, verifying at each stage: (a) the correct `executorState.steps` array is returned with the right artefacts, (b) the timeline data would produce the expected `TimelineStep[]` shape, (c) signal submission with the panel's payload shape succeeds and advances to the next checkpoint. This is the AC-9 end-to-end verification.

**Rationale:**
Individual panel tests and projection tests verify pieces. This test verifies the full start→brief→tasks→assignment→plan→implementation→review→complete flow with enriched state at each transition.

**Acceptance Criteria:**
- [ ] Test drives a work item from `Running` through all six checkpoints to `Completed`
- [ ] At each pause, verifies `executorState.steps` length, active step key, and artefact presence
- [ ] Signal payloads match the shapes the new panels would submit
- [ ] Loop-back from `review-completed` to `assignment-confirmed` for a second task is tested
- [ ] Test passes with `dotnet test`

**Files to Modify/Create:**
- `tests/DevHub.Modules.WorkItems.Tests/Integration/LifecycleCheckpointProgressionTests.cs` — new test class
- `tests/DevHub.TestHarness/FakeOrchestrator/` — may need additional scripted responses for full lifecycle

**Technical Notes:**
Use the existing `FakeOrchestratorHost` pattern from FEAT-010/T-088. Script a multi-step run with pauses at each checkpoint type. The FakeOrchestrator needs to emit trace records with artefact data at each step.

---

## Documentation

### T-108: Documentation updates — ui-specification, ARCHITECTURE, brief status

**Type:** Documentation
**Workflow:** standard
**Complexity:** S
**Dependencies:** T-104

**Description:**
Update documentation to reflect the new per-checkpoint panel architecture: (a) `docs/ui-specification.md` — add component entries for the five new panels, update the review page screen spec to describe the checkpoint switch, (b) `docs/ARCHITECTURE.md` — note the per-checkpoint panel pattern in the frontend architecture section, (c) `docs/work-items/FEAT-011-first-class-lifecycle-screens.md` — update Status to reflect completion.

**Rationale:**
Documentation maintenance discipline per CLAUDE.md — new components and changed screens must be reflected in the specs.

**Acceptance Criteria:**
- [ ] `docs/ui-specification.md` lists all five new panel components with their purpose and data inputs
- [ ] Review page screen spec describes the checkpoint routing switch
- [ ] `docs/ARCHITECTURE.md` mentions the per-checkpoint panel pattern
- [ ] Changelog entries added to updated docs
- [ ] FEAT-011 brief status updated

**Files to Modify/Create:**
- `docs/ui-specification.md` — new component entries + review page update + changelog
- `docs/ARCHITECTURE.md` — frontend architecture note + changelog
- `docs/work-items/FEAT-011-first-class-lifecycle-screens.md` — status update

---

## Summary

| Metric | Value |
|--------|-------|
| **Total tasks** | 12 (T-097 – T-108) |
| **By type** | Investigation: 1, Backend: 1, Frontend: 6 (5 panels + 1 wiring), Testing: 3, Documentation: 1 |
| **Complexity distribution** | S: 3, M: 6, L: 3 |
| **Critical path** | T-097 → T-098 → T-099..T-103 (parallel) → T-104 → T-107 |

### Critical Path

```
T-097 (investigate trace shapes)
  └→ T-098 (enrich ExecutorStateProjection)
       ├→ T-099 (BriefReviewPanel)      ─┐
       ├→ T-100 (TaskListReviewPanel)    ─┤
       ├→ T-101 (PlanReviewPanel)        ─┤ parallel
       ├→ T-102 (ImplementationReviewPanel)┤
       ├→ T-103 (FinalReviewPanel)       ─┘
       └→ T-106 (backend tests)
            └→ T-107 (integration test)
       T-099..T-103 ──→ T-104 (review page wiring)
                          ├→ T-105 (panel unit tests)
                          ├→ T-107 (integration test)
                          └→ T-108 (documentation)
```

### Risks and Open Questions

1. **Artefact availability in trace.** The biggest risk is T-097: if the orchestrator's trace doesn't emit node output artefacts (only step metadata), the panels would have nothing to render beyond the generic "waiting on checkpoint" state. Mitigation: if artefacts aren't in the trace, evaluate adding a `/api/v1/runs/{id}/steps/{stepId}` endpoint on the orchestrator side (separate FEAT on that repo).

2. **Signal payload shapes.** The panels construct payloads matching what the orchestrator expects. If the orchestrator's signal validators reject DevHub's payload shapes, the signals will fail at runtime. Mitigation: T-097 should also document the expected signal payload schemas per checkpoint.

3. **`TaskListReviewPanel` complexity.** The drag-and-drop / reorder + inline editing UX is the most complex panel. If it proves too heavy for v1, fall back to a simpler "review and approve as-is" panel with a feedback textarea for requested changes (same pattern as the other panels).
