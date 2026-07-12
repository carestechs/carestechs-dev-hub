# FEAT-017 — Mockup Checkpoint

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-017 |
| **Name** | Mockup Checkpoint — `confirm_mockup` screen and task `kind` badge (`lifecycle-agent@0.5.0-manual`) |
| **Target Version** | Continuous |
| **Status** | Completed |
| **Priority** | High |
| **Requested By** | Carlos |
| **Date Created** | 2026-07-11 |
| **Depends On** | FEAT-011, FEAT-012 |

---

## 2. User Story

**As an** operator reviewing a task whose kind is `"mockup"`, **I want** to see the HTML mockup the agent generated before the implementation plan is written, **so that** I can approve the visual direction or reject it with feedback — keeping the agent from wasting time planning against a screen layout I would never accept.

---

## 3. Goals

Wire DevHub to `lifecycle-agent@0.5.0-manual`. The new version inserts a `confirm_mockup` checkpoint between assignment and planning for tasks with `kind="mockup"`. All existing checkpoints are unchanged.

Two concrete deliverables:

1. **Mockup review screen.** When `currentCheckpointKey === "confirm_mockup"`, show the generated HTML mockup in a sandboxed iframe alongside a description, an approve button, and a reject path with a feedback field.
2. **Task kind badge.** Surface a visual indicator on tasks with `kind="mockup"` in the task list view so the operator knows before approving the task list that a mockup step is coming.

---

## 4. Feature Scope

### 4.1 Included

#### Backend

- **Register `confirm_mockup` checkpoint contract.** `allowedOutcomes: ["approve", "reject"]`. Seeded via the checkpoint-contract admin API or data migration alongside the existing contracts.
- **Signal name mapping.** `OrchestratorExecutorClient` maps `checkpointKey="confirm_mockup"` → orchestrator signal `name="mockup-approved"`. All other checkpoint→signal mappings are unchanged.
- **`CheckpointDerivation`.** Map raw orchestrator checkpoint key `confirm_mockup` → DevHub checkpoint key `confirm_mockup` (identity mapping; no slug transformation needed).

#### Frontend — `MockupReviewPanel`

- New standalone Angular component `MockupReviewPanel` (`mockup-review-panel.ts` / `.html`).
- Renders `nodeInputs.mockupHtml` inside a sandboxed `<iframe srcdoc="...">` — never injected directly into the DOM.
- Shows `nodeInputs.mockupDescription` as a caption below the iframe.
- Shows `nodeInputs.currentTask.title` and `nodeInputs.currentTask.description` as context above the iframe.
- **Approve path:** single "Looks good" button → `{ outcome: "approve" }`.
- **Reject path:** "Request changes" button reveals a textarea for feedback → `{ outcome: "reject", payload: { feedback: "..." } }`. Submit disabled while feedback is empty.
- Wired into `review.page.html` `@switch` block as the `confirm_mockup` case.

#### Frontend — task kind badge in `TaskListReviewPanel`

- Tasks with `kind="mockup"` show a small `Mockup` badge (e.g. sky-100/sky-700) next to the task title.
- Tasks with other kinds (`"feature"`, `"bug"`, `"chore"`) show no badge (kind is implicit).
- Badge is purely informational — it does not change the signal or approve/reject flow.

### 4.2 Excluded

- **Mockup storage / history.** The `mockupHtml` is passed through `nodeInputs` at the active checkpoint only; DevHub does not persist it. Future versions may store mockup revisions.
- **Mockup diff view.** No side-by-side comparison between the original and revised mockup after a rejection loop.
- **Kind-based routing in task creation.** DevHub does not set or override `kind` on tasks — it is set by the orchestrator when generating the task list.
- **Admin UI for checkpoint contract registration.** Seeded as part of executor registration, not a new UI surface.

---

## 5. Acceptance Criteria

- **AC-1:** When a work item's `currentCheckpointKey` is `confirm_mockup`, the review page renders `MockupReviewPanel` — not the generic `CheckpointActionBar`.
- **AC-2:** `mockupHtml` is rendered inside `<iframe srcdoc>` with `sandbox` attribute; it never touches the outer DOM.
- **AC-3:** Approving sends `{ outcome: "approve" }` to `POST .../checkpoints/confirm_mockup/signal` and the work item advances to `generate_plan`.
- **AC-4:** Rejecting with feedback sends `{ outcome: "reject", payload: { feedback: "..." } }` and the work item cycles back to `generate_mockup`.
- **AC-5:** Tasks with `kind="mockup"` in `confirm_tasks` nodeInputs display a `Mockup` badge in `TaskListReviewPanel`.
- **AC-6:** Tasks with `kind="feature"`, `"bug"`, or `"chore"` display no kind badge.
- **AC-7:** The reject submit button is disabled when the feedback textarea is empty.
- **AC-8:** All existing checkpoint flows (`brief-confirmed`, `tasks-confirmed`, `assignment-confirmed`, `plan-confirmed`, `implementation-complete`, `review-completed`) are unaffected.

---

## 6. Key Entities and Business Rules

| Entity | Change | Rule |
|--------|--------|------|
| Checkpoint contract | New row: `confirm_mockup` | `allowedOutcomes: ["approve", "reject"]`; required role: operator |
| Signal mapping | `confirm_mockup` → `mockup-approved` | Identity mapping in `OrchestratorExecutorClient` |
| `nodeInputs` at `confirm_mockup` | Read-only | `mockupHtml`, `mockupDescription`, `currentTask` — no persistence in DevHub |

---

## 7. API Impact

| Endpoint | Change |
|----------|--------|
| `POST .../checkpoints/confirm_mockup/signal` | New endpoint (handled by existing generic signal handler); payload may carry `feedback: string` on reject |

No new endpoints. The generic checkpoint signal endpoint already handles any registered `checkpointKey`.

---

## 8. UI Impact

| Screen / Component | Change |
|--------------------|--------|
| `review.page.html` | Add `confirm_mockup` case to `@switch` block → `<mockup-review-panel>` |
| `review.page.ts` | Import `MockupReviewPanel` |
| `MockupReviewPanel` | New component — mockup iframe, description, approve/reject with feedback |
| `TaskListReviewPanel` | Add `kind="mockup"` badge rendering; no change to signal flow |

---

## 9. Dependencies

| Dependency | Direction |
|------------|-----------|
| FEAT-011 — First-Class Lifecycle Screens | Must be complete; `review.page` `@switch` pattern is the integration point |
| `lifecycle-agent@0.5.0-manual` | Orchestrator must be on this version for `confirm_mockup` to fire |

---

## 10. Motivation and Priority Justification

Mockup tasks are high-stakes: committing to an implementation plan before the operator has seen the proposed UI means a failed review at `implementation-complete` and a full rework cycle. The `confirm_mockup` checkpoint is the correct place to catch layout/UX misalignment — cheap to change a mockup, expensive to change a built screen. DevHub's role is to surface it cleanly and route the operator's feedback back to the agent without friction.

---

## 11. Acceptance Criteria (Tests)

- [ ] Unit test: `MockupReviewPanel` renders iframe with `srcdoc` equal to `mockupHtml`; approve emits `{ outcome: "approve" }`; reject with feedback emits `{ outcome: "reject", payload: { feedback: "..." } }`.
- [ ] Unit test: `TaskListReviewPanel` renders `Mockup` badge for `kind="mockup"` tasks and no badge for other kinds.
- [x] Integration test: signal `confirm_mockup` with `outcome="approve"` returns 200 and advances the work item.
- [x] Integration test: signal `confirm_mockup` with `outcome="reject"` and `feedback` returns 200.

---

## 12. Traceability

| Reference | Value |
|-----------|-------|
| Orchestrator spec | `docs/lifecycle-v05-manual-devhub-integration.md` in `carestechs-agent-orchestrator` |
| Depends on | FEAT-011, FEAT-012 |
| Lifecycle version | `lifecycle-agent@0.5.0-manual` |
