# Feature Brief: FEAT-011 — First-Class Lifecycle-Agent Review Screens

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-011 |
| **Name** | First-Class Lifecycle-Agent Review Screens (`lifecycle-agent@0.4.0-manual`) |
| **Target Version** | v1 |
| **Status** | Completed |
| **Priority** | High |
| **Requested By** | Operator (replace generic checkpoint UI with purpose-built screens per lifecycle stage) |
| **Date Created** | 2026-05-24 |

## 2. User Story

**As an** operator reviewing a `lifecycle-agent@0.4.0-manual` work item through DevHub, **I want** each checkpoint to present a purpose-built review screen that understands the artefact and payload at that stage — not a generic "pick an outcome and submit" form — **so that** I can review briefs inline, curate task lists, assign tasks, inspect plans, review implementation, and sign off, all with controls that match the data I'm looking at.

## 3. Goal

Replace the generic `CheckpointActionBar` (allowed outcomes + notes textarea) with dedicated Angular components for each of the six checkpoint types in `lifecycle-agent@0.4.0-manual`. Each component knows the shape of the data the orchestrator produces at its stage and presents appropriate editing / review / approval controls.

The `AssignmentConfirmPanel` (FEAT-009) is the first example of this pattern — it already replaced the generic bar for `assignment-confirmed`. This FEAT extends the same treatment to the remaining five checkpoints and builds the plumbing to feed them the right data.

### What changes

1. **Backend: richer `executorState` projection.** Today `ExecutorStateProjection.Build()` produces `{ runId, agentRef, lastStep, assignments, stopReason }`. The `lastStep` object is the orchestrator's raw step DTO — it doesn't carry the node's output artefact (the brief text, the task list, the plan). To power purpose-built screens, the projection needs to extract and surface the artefact that the agent produced at each completed step, sourced from the orchestrator's trace records. The exact shape depends on what the orchestrator's trace emits per node — this FEAT includes an investigation phase to map the trace schema.

2. **Backend: trace-derived timeline.** The frontend's `LifecycleTimeline` reads `executorState.steps` (an array), but the current projection only produces `lastStep` (a single object). The projection must build a `steps` array from the trace records — one entry per completed or active step — so the timeline renders the full lifecycle progression.

3. **Frontend: per-checkpoint review panels.** Five new components (one per remaining checkpoint type) replace the generic `CheckpointActionBar` on the review page. The review page routes to the correct panel based on `currentCheckpointKey`.

4. **Frontend: the generic `CheckpointActionBar` stays** as the fallback for any checkpoint key that doesn't match a known lifecycle-agent checkpoint. This covers future agents, test executors, and any unexpected checkpoint from the orchestrator.

## 4. Feature Scope

### 4.1 Included

#### Backend — `ExecutorStateProjection` enrichment

- **`steps` array in `executorState`.** Built from the orchestrator's trace records. Each entry:
  ```json
  {
    "key": "brief-confirmed",
    "nodeName": "confirm_brief",
    "label": "Brief Review",
    "stepNumber": 2,
    "status": "completed",
    "artefact": { ... }
  }
  ```
  `artefact` is the node's output extracted from the trace — its shape varies by checkpoint type (see §6). Steps are ordered by `stepNumber`. The currently active step has `status: "active"`.

- **Artefact extraction per checkpoint type.** The projection walks trace records and extracts the agent's output for each completed step. The exact trace fields to read will be determined during the investigation phase (T-100). Known expectations based on the agent's behavior:
  - `brief-confirmed`: the generated brief (markdown text — title, type, summary, scope).
  - `tasks-confirmed`: the generated task list (array of task objects with id, title, description).
  - `assignment-confirmed`: already handled — `{ taskId, assignee }` from signal records.
  - `plan-confirmed`: the generated plan for the current task (markdown text).
  - `implementation-complete`: implementation summary + reference to the work branch / PR.
  - `review-completed`: the review verdict and feedback from the agent's review step.

- **`DisplayLabels` map.** A static mapping from checkpoint keys to human-readable labels:
  | Checkpoint Key | Label |
  |---|---|
  | `brief-confirmed` | Brief Review |
  | `tasks-confirmed` | Task List |
  | `assignment-confirmed` | Task Assignment |
  | `plan-confirmed` | Plan Review |
  | `implementation-complete` | Implementation Review |
  | `review-completed` | Final Review |

#### Frontend — per-checkpoint review panels

- **`BriefReviewPanel`** (`brief-review-panel.ts`):
  - Renders the LLM-generated brief as formatted markdown (reuses `MarkdownRenderer`).
  - Shows the proposed title and work-item type in editable fields (operator can override before confirming).
  - Actions: **Confirm** (sends the brief with any title/type overrides as payload) or **Reject** (sends feedback in a textarea for the agent to regenerate).

- **`TaskListReviewPanel`** (`task-list-review-panel.ts`):
  - Renders the generated task list as an editable list.
  - Each task shows id, title, description. Operator can:
    - **Reorder** tasks (drag-and-drop or up/down buttons).
    - **Remove** tasks from the list.
    - **Add** a new task (inline form: title + description).
    - **Edit** task title/description inline.
  - Actions: **Confirm** (sends the curated task list as payload) or **Reject** (sends feedback).

- **`PlanReviewPanel`** (`plan-review-panel.ts`):
  - Renders the generated implementation plan as formatted markdown.
  - Shows which task the plan is for (task id + title from `currentTaskId`).
  - Actions: **Confirm** or **Reject** (with feedback textarea).

- **`ImplementationReviewPanel`** (`implementation-review-panel.ts`):
  - Shows the implementation summary from the agent.
  - Displays the work branch name and (if available) a link to the PR.
  - Embeds the `DiffViewer` component if the artefact includes diff data.
  - Actions: **Complete** (marks implementation as done — operator confirms the code is ready for review) or **Request Changes** (sends feedback for the agent to iterate).

- **`FinalReviewPanel`** (`final-review-panel.ts`):
  - Shows the agent's self-review findings (what it tested, what passed, what concerns remain).
  - Displays the task context (which task this review covers).
  - Actions: **Approve** (task is done, triggers loop-back to next task's assignment if remaining) or **Reject** (with feedback — agent re-implements).

- **`ReviewPage` routing logic update.** The existing `@if (isAssignmentConfirmCheckpoint())` conditional expands to a `@switch (currentCheckpointKey)` that renders the matching panel. Unknown keys fall back to `CheckpointActionBar`.

#### Frontend — timeline fix

- **`LifecycleTimeline` now reads from `executorState.steps` (the new array).** Since the backend now populates this as an ordered array with `key`, `label`, `state`, and optionally the signal info, the timeline will render the full lifecycle progression for orchestrator-backed work items.

### 4.2 Excluded

- **Non-manual variants.** Only `lifecycle-agent@0.4.0-manual` checkpoint shapes are covered. Future automated variants will be a separate FEAT.
- **Generic executor UI redesign.** The generic `CheckpointActionBar` stays as-is. No changes to the devhub-protocol path.
- **Real-time artefact updates within a step.** Artefacts are fetched on page load (via `FetchStateAsync`) and after signal submission. No mid-step live push of partial artefacts.
- **Inline code editing in `ImplementationReviewPanel`.** The panel displays diffs read-only. Actual code changes happen in the operator's IDE.
- **GitHub PR integration.** PR links in `ImplementationReviewPanel` are display-only (rendered from the artefact data if present). No GitHub API calls from DevHub.
- **Backend changes to signal payloads.** The orchestrator's signal API shape stays unchanged. The new panels construct their payloads client-side from the form state and submit via the existing `WorkItemsService.signal()` path.
- **Persisting operator edits (title override, task reordering) in DevHub.** The edited values travel as signal payload to the orchestrator. DevHub doesn't store a shadow copy.

## 5. Acceptance Criteria

- **AC-1 (Brief review):** When a work item pauses at `brief-confirmed`, the review page renders `BriefReviewPanel` showing the generated brief as markdown, with editable title and type fields. Confirming sends `{ title, type, notes }` as payload. Rejecting sends `{ feedback }`.
- **AC-2 (Task list review):** When paused at `tasks-confirmed`, `TaskListReviewPanel` renders the task list. The operator can reorder, add, remove, and edit tasks. Confirming sends the curated list as payload.
- **AC-3 (Assignment):** `assignment-confirmed` continues to use `AssignmentConfirmPanel` (FEAT-009). No regression.
- **AC-4 (Plan review):** When paused at `plan-confirmed`, `PlanReviewPanel` renders the plan as markdown with the task context. Confirm/reject works.
- **AC-5 (Implementation review):** When paused at `implementation-complete`, `ImplementationReviewPanel` shows the summary, branch name, and diff (if available). Complete/request-changes works.
- **AC-6 (Final review):** When paused at `review-completed`, `FinalReviewPanel` shows the agent's review findings. Approve/reject works and triggers loop-back for remaining tasks.
- **AC-7 (Fallback):** An unknown `currentCheckpointKey` (e.g. from a test executor or future agent) still renders the generic `CheckpointActionBar`. No regression.
- **AC-8 (Timeline):** The `LifecycleTimeline` shows all completed and active steps with correct state (`approved`, `active`, `pending`) for orchestrator-backed work items. Clicking a past step shows its artefact.
- **AC-9 (End-to-end):** A `lifecycle-agent@0.4.0-manual` work item driven from start to completion uses all six purpose-built panels in sequence, with the timeline progressing and artefacts rendering at each stage.
- **AC-10 (Authorization):** Each panel respects the checkpoint contract's `requiredRoleKey`. A member without the required role sees the artefact (read-only) but cannot submit.

## 6. Key Entities and Business Rules

### Artefact shapes per checkpoint (verified — T-097 investigation, 2026-05-24)

The artefact data the operator reviews at each human checkpoint lives in the `nodeResult` of the **preceding LLM/generation step** in the trace. Human checkpoint steps themselves have `nodeResult: null` while paused (waiting for the operator's signal); after signal delivery, their `nodeResult` carries the operator's signal payload.

| Checkpoint Key | Artefact Source (preceding step) | `nodeResult` Shape | Schema Reference |
|---|---|---|---|
| `brief-confirmed` | `load_work_item` step (LLM) | `{ work_item_id: string, title: string, summary: string }` | `LoadWorkItemResult` (`lifecycle_schemas.py`) |
| `tasks-confirmed` | `generate_tasks` step (LLM) | `{ tasks: [{ id, title, executor, description, acceptance_criteria, complexity, depends_on, files_hint }] }` | `GenerateTasksResult` (`lifecycle_schemas.py`) |
| `assignment-confirmed` | No preceding generation step | N/A (operator-initiated — operator picks assignee) | — |
| `plan-confirmed` | `generate_plan` step (composite LLM+engine) | `{ task_id: string, plan_markdown: string }` | `GeneratePlanResult` (`lifecycle_schemas.py`) |
| `implementation-complete` | No preceding generation step | N/A (operator signals when implementation is done; payload carries `{ commitSha?, prUrl?, summary? }`) | — |
| `review-completed` | No preceding generation step in manual variant | N/A (operator reviews code; payload carries `{ verdict: "pass"\|"fail", feedback? }`) | — |

**`__memory_patch`** is stripped from `nodeResult` in the projection (T-098) — it's an internal runtime concern and can be large. The frontend never sees it.

**Graceful degradation:** panels must handle `nodeResult: null` (active checkpoint, not yet signaled), missing fields (schema evolution), and unexpected shapes (fallback to `ArtefactFallback`).

### `executorState.steps` array structure (implemented — T-098)

The `steps` array includes **all** `kind == "step"` trace records (LLM steps, engine steps, and human checkpoint steps), ordered by appearance in the trace. Human checkpoint steps have a non-null `key` (derived via `CheckpointDerivation`); internal steps (LLM, engine) have `key: null`. The frontend reads artefact data from the preceding step's `nodeResult` for each checkpoint panel.

```json
{
  "runId": "uuid",
  "agentRef": "lifecycle-agent@0.4.0-manual",
  "steps": [
    { "key": null, "nodeName": "load_work_item", "label": "load_work_item", "stepNumber": 1, "status": "completed", "nodeResult": { "work_item_id": "FEAT-1", "title": "Add CSV export", "summary": "..." } },
    { "key": "brief-confirmed", "nodeName": "confirm_brief", "label": "Brief Review", "stepNumber": 2, "status": "completed", "nodeResult": { "signalName": "brief-confirmed", "title": "Add CSV export", "type": "FEAT" } },
    { "key": null, "nodeName": "generate_tasks", "label": "generate_tasks", "stepNumber": 3, "status": "completed", "nodeResult": { "tasks": [{ "id": "T-1", "title": "..." }] } },
    { "key": "tasks-confirmed", "nodeName": "confirm_tasks", "label": "Task List", "stepNumber": 4, "status": "active", "nodeResult": null }
  ],
  "assignments": { "T-001": "Alice" },
  "stopReason": null
}
```

### Panel → signal mapping

| Panel | Outcome | Signal Payload |
|---|---|---|
| `BriefReviewPanel` | `confirmed` | `{ title?, type?, notes? }` |
| `BriefReviewPanel` | `rejected` | `{ feedback }` |
| `TaskListReviewPanel` | `confirmed` | `{ tasks: [{ id, title, description }] }` |
| `TaskListReviewPanel` | `rejected` | `{ feedback }` |
| `AssignmentConfirmPanel` | `confirmed` | `{ assignee, taskId }` |
| `PlanReviewPanel` | `confirmed` | `{ notes? }` |
| `PlanReviewPanel` | `rejected` | `{ feedback }` |
| `ImplementationReviewPanel` | `complete` | `{ notes? }` |
| `ImplementationReviewPanel` | `changes-requested` | `{ feedback }` |
| `FinalReviewPanel` | `approved` | `{ notes? }` |
| `FinalReviewPanel` | `rejected` | `{ feedback }` |

## 7. API Impact

- **No new endpoints.** All signal submission goes through the existing `POST /api/projects/{pid}/work-items/{wid}/signal` endpoint. The new panels construct richer payloads, but the signal wire format (`{ outcome, payload, taskId? }`) is unchanged.
- **`executorState` shape change.** The JSON blob returned in `WorkItemDto.executorState` gains a `steps` array (replacing the previous `lastStep` single object). This is a non-breaking change — `executorState` is documented as opaque JSON, and the frontend is the only consumer.

## 8. UI Impact

- **Review page (`review.page.ts`):** Replaces the binary `@if (isAssignmentConfirmCheckpoint())` / `@else` with a `@switch` over `currentCheckpointKey`. Six known keys route to their panels; default routes to `CheckpointActionBar`.
- **Five new components** under `client/src/app/features/projects/work-items/components/`:
  - `brief-review-panel.ts` + `brief-review-panel.html`
  - `task-list-review-panel.ts` + `task-list-review-panel.html`
  - `plan-review-panel.ts` + `plan-review-panel.html`
  - `implementation-review-panel.ts` + `implementation-review-panel.html`
  - `final-review-panel.ts` + `final-review-panel.html`
- **`LifecycleTimeline`:** No API change, but now receives populated `steps` data from the enriched `executorState`. Existing rendering logic works as-is.
- **`ActiveStepArtefactPanel`:** Now receives checkpoint-type-specific artefacts. The existing `kind`-based dispatch (`diff`, `markdown`, `fallback`) may need a `task-list` variant added.
- **No changes to:** Home page, operator dashboard, admin screens, work-item detail page, start-work modal.

## 9. Edge Cases

- **Orchestrator trace doesn't include the expected artefact fields.** Panels degrade to `ArtefactFallback` (JSON pretty-print). No crash, no blank panel.
- **Agent version changes artefact shape between runs.** Each panel reads defensively (optional chaining, type guards). Unexpected fields are ignored; missing fields show placeholder text.
- **Work item started before FEAT-011 (no `steps` array in cached state).** `LifecycleTimeline` falls back to an empty timeline. The review panel still renders based on `currentCheckpointKey`.
- **Operator navigates to the review page while the agent is mid-step (not paused).** No active checkpoint → no panel rendered. The timeline shows the last completed step. The live trace feed provides real-time progress.
- **Two browser tabs on the same work item; one submits a signal.** The other tab's panel becomes stale. On the next `refetchOnly()` (triggered by SSE `checkpointResolved` event), the page updates to the new checkpoint and swaps the panel.
- **Operator reorders tasks in `TaskListReviewPanel` then navigates away without submitting.** Changes are lost (no draft persistence). Intentional — the authoritative list is the agent's; only a submitted signal mutates state.
- **`CheckpointActionBar` receives an empty `allowedOutcomes` array.** Already handled — the bar renders read-only. No regression from this FEAT.

## 10. Constraints

- **Investigation-first workflow.** The artefact shapes in §6 are expectations based on the agent's known behavior. The first task must investigate the orchestrator's actual trace output per node type and adjust the panel designs accordingly before implementation begins.
- **No new backend endpoints.** All data flows through the existing `FetchStateAsync` → `executorState` path. The enrichment happens inside `ExecutorStateProjection`.
- **Panels are read-only viewers with action buttons — not editors that persist state.** The only mutation is the signal submission. All "editing" (title override, task reorder) is ephemeral form state that becomes the signal payload.
- **Graceful degradation is mandatory.** Every panel must work even if the artefact is null, empty, or shaped differently than expected. The fallback is `ArtefactFallback` (JSON dump) or a "no artefact available" message.
- **`AssignmentConfirmPanel` is untouched.** It already works. This FEAT does not modify it.

## 11. Motivation and Priority Justification

**Motivation:** The generic `CheckpointActionBar` presents every checkpoint as "pick an outcome from a list of strings and optionally type some notes." This is technically functional but operationally useless for a lifecycle agent where each checkpoint has rich, structured data:

- A brief review should show the brief and let the operator edit the title — not present "approve / reject" with a notes box.
- A task list review should let the operator reorder and edit tasks — not show a JSON blob.
- A plan review should render the plan as formatted markdown — not dump it as raw text.

Every time an operator uses the generic bar, they lose context and have to mentally translate between what they're looking at (raw JSON in the artefact panel) and what they're deciding. Purpose-built panels close that gap.

The `AssignmentConfirmPanel` (FEAT-009) proved the pattern works — it replaced the generic bar for one checkpoint and immediately improved the operator experience. This FEAT extends the same treatment to the remaining five.

**Impact if delayed:** DevHub can technically drive lifecycle-agent work items today, but the experience is so generic that it barely improves on curling the orchestrator directly. The "single front door" thesis only delivers value when the front door is better than the raw API.

**Dependencies:** Builds on FEAT-009 (per-task assignment panel) and FEAT-010 (orchestrator client). Requires read access to the carestechs-agent-orchestrator's trace format (investigation phase).

## 12. Traceability

| Reference | Link |
|-----------|------|
| **Persona** | Operator |
| **Stakeholder Scope Item** | "Lifecycle-specific review screens land as new screens in DevHub without altering any executor" (Success Criteria §5). |
| **Success Metric** | An operator reviews and advances a `lifecycle-agent@0.4.0-manual` work item through all six checkpoints using purpose-built screens, without needing to interpret raw JSON or remember payload schemas. |
| **Related Work Items** | FEAT-009 (per-task assignment — the first specific panel), FEAT-010 (orchestrator client — provides the trace data), FEAT-004 (work items façade — the signal path). |
| **Design direction** | Project memory: `first-class-lifecycle-agent` — DevHub treats lifecycle-agent@0.4.0-manual as primary; generic layer is fallback. |
