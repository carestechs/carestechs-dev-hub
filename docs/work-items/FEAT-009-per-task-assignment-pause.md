# Feature Brief: FEAT-009 — Per-Task Assignment Pause (`assignment-confirmed`)

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-009 |
| **Name** | Per-Task Assignment Pause (`assignment-confirmed`) |
| **Target Version** | v1 |
| **Status** | Not Started |
| **Priority** | Medium |
| **Requested By** | Operator (orchestrator IMP-005 manual-variant fifth checkpoint) |
| **Date Created** | 2026-05-17 |

## 2. User Story

**As an** operator running `lifecycle-agent@0.4.0-manual` work items through DevHub, **I want to** confirm the human assignee for each task before the executor plans it — and be re-prompted after every `review-completed` when more tasks remain — **so that** task ownership is explicit, audited, and surfaced in the dashboard, not inferred from a single up-front decision at work-item creation.

## 3. Goal

Surface the new `assignment-confirmed` checkpoint that the orchestrator introduced in IMP-005 (manual variant only). Today DevHub's checkpoint contracts assume each named pause fires at most once per work item. The new pause fires **once per task**, with a loop-back through `confirm_assignment` after `mark_task_done`. The operator must deliver one `assignment-confirmed` signal per task, with a non-empty `payload.assignee`, in the sequence:

```
brief-confirmed → tasks-confirmed → assignment-confirmed → plan-confirmed → implementation-complete → review-completed
                                          ↑                                                                  │
                                          └──────── (loops back here per remaining task) ─────────────────┘
```

Make this work in the DevHub façade end-to-end: signal forwarding, per-task checkpoint identity, pending-action surfacing, operator UI for assignee entry, and audit linkage.

## 4. Feature Scope

### 4.1 Included

- **Checkpoint contract registration** for `assignment-confirmed` on the lifecycle-agent manual flow:
  - Display name: "Confirm task assignment".
  - Required payload shape declared in the contract: `{ assignee: string (non-empty), taskId?: string }`.
  - `perTask: true` flag on the `CheckpointContract` row (new column — see §6) so the contract registry knows this is a repeating pause keyed by `(work_item_id, task_id)` rather than `(work_item_id, checkpoint_key)`.
- **Per-task pending-action identity**:
  - `PendingAction` gains a nullable `task_id` column.
  - Uniqueness of an active pending action becomes `(work_item_id, checkpoint_key, COALESCE(task_id, '<root>'))`.
  - SSE event payload (`PendingActionEvent`) gains `taskId` so the SPA can disambiguate.
- **Signal forwarding**:
  - `WorkItemsService.SignalAsync` accepts an optional `taskId` from the caller and includes it in the payload to the executor.
  - Idempotency on the executor side is keyed by `(run_id, "assignment-confirmed", task_id)`; DevHub trusts that and does not dedupe locally.
- **Operator UI**:
  - **Lifecycle-screen "assignment-confirmed" panel**: renders a member-picker scoped to the work item's project memberships (plus an "Other (free text)" escape hatch since `assignee` is just a string on the executor side). Submit posts the signal with `payload.assignee` + `taskId`.
  - **Pending-actions panel** (Operator dashboard + sidebar badge): each per-task pause appears as its own row, labeled `<work item title> — <task id>` and routes to the per-task screen on click.
  - **Work item detail** gains an "Assignments" sidebar listing `taskId → assignee`, sourced from the `RunMemory.data.assignments` sidecar the orchestrator now writes (read-through via the existing fetch-state endpoint; no DevHub-side persistence in v1).
- **Loop-back semantics**:
  - After `review-completed` for task N, the orchestrator re-enters `confirm_assignment` for task N+1 (if any). DevHub already supports closing and re-opening pending actions for the same checkpoint key on the same work item via the executor's trace stream — the only addition is `taskId` discrimination so the dashboard shows the *new* task's pending action rather than re-opening the previous task's row.
- **Audit**:
  - `assignment-confirmed` signal forwards write a `workitem:signal` audit entry with `details = { checkpointKey, taskId, assignee, outcome }`. `assignee` is treated as PII-adjacent but not sensitive — full value stored, no truncation needed at typical lengths.
- **Doc updates**:
  - `docs/data-model.md` — `CheckpointContract.perTask` column, `PendingAction.task_id` column, changelog entry.
  - `docs/api-spec.md` — `PendingActionDto.taskId`, signal endpoint `taskId` field, changelog entry.
  - `docs/ui-specification.md` — lifecycle-screen "assignment-confirmed" mockup, dashboard row variant, changelog entry.
  - `CLAUDE.md` — pattern note on per-task checkpoints (new concept beyond the one-pause-per-work-item assumption).

### 4.2 Excluded

- **Non-manual variants.** This pause exists only on `lifecycle-agent@0.4.0-manual`. The `0.3.0` flow is byte-unchanged per the IMP-005 notes and continues to work without `assignment-confirmed`.
- **DevHub-side dedupe.** Idempotency lives in the executor on `(run_id, checkpoint, task_id)`. We don't double-guard.
- **Member-to-orchestrator-handle mapping.** `payload.assignee` is a free-form string from the executor's point of view. DevHub's member-picker writes the member's `displayName` or `email` (operator's choice in the UI) — there's no executor-side identity registry to reconcile against.
- **Forced assignment** (auto-fill from previous task's assignee). The IMP-005 spec notes "previous assignees are preserved across the loop-back" inside the executor's memory — but each pause still requires a fresh, explicit operator signal. We do not pre-submit on the operator's behalf.
- **Bulk-assign across all remaining tasks.** Each task is its own decision; v1 does not offer "assign all remaining to Alice."
- **Reassignment after `assignment-confirmed` is sent.** Once the signal is forwarded for a task, that task is past the pause; changing assignees mid-task would require an executor-side concept we don't have.
- **Per-task assignee persistence in DevHub's own tables.** We rely on the executor's `RunMemory.data.assignments` sidecar via fetch-state. No `Assignment` table.

## 5. Acceptance Criteria

- **AC-1:** Registering an executor whose checkpoint-contract list includes `assignment-confirmed` with `perTask: true` succeeds. The contract row is written with the new flag set. Existing contracts default to `perTask: false`; this is a schema-level default, not a migration concern.
- **AC-2:** When the executor's trace stream emits `PendingActionRaised { checkpoint: "assignment-confirmed", taskId: "T-001" }`, DevHub creates a `PendingAction` row keyed by `(work_item_id, "assignment-confirmed", "T-001")` and broadcasts an SSE event with `taskId="T-001"`.
- **AC-3:** A second `PendingActionRaised { checkpoint: "assignment-confirmed", taskId: "T-002" }` on the same work item creates a **distinct** pending action (not an update to the T-001 row). Both can coexist if the executor ever fans them out (the spec says it doesn't today, but the data model must allow it).
- **AC-4:** From the lifecycle-screen page, an operator can submit `assignment-confirmed` with assignee=`alice` and taskId=`T-001`. DevHub forwards `POST /work-items/{marker}/checkpoints/assignment-confirmed/signal` with `payload = { assignee: "alice", taskId: "T-001" }` and closes the corresponding pending-action row on the executor's acknowledgement event.
- **AC-5:** Submitting `assignment-confirmed` with empty `assignee` returns `400` from DevHub's signal endpoint with a `ValidationException` problem detail. No executor call attempted. Denied audit entry written.
- **AC-6:** The Operator dashboard's "Pending on you" panel renders per-task rows distinctly: `Add CSV export — T-001`, `Add CSV export — T-002`. Clicking a row routes to the lifecycle screen with the correct `taskId` pre-selected.
- **AC-7:** The badge count in the sidebar reflects the total count of distinct pending-action rows, including per-task pauses. A work item with three remaining `assignment-confirmed` tasks contributes 3, not 1.
- **AC-8:** Loop-back works end-to-end with a fake executor: after submitting `review-completed` for T-001, the fake executor emits `PendingActionRaised { checkpoint: "assignment-confirmed", taskId: "T-002" }`. DevHub creates a fresh row for T-002 and surfaces it on the dashboard. The T-001 row remains closed (does not re-open).
- **AC-9:** The work item detail page's "Assignments" sidebar lists `T-001 → alice` after AC-4. The data source is a fetch-state call to the executor (which returns `RunMemory.data.assignments`), not a DevHub-side table.
- **AC-10:** A signal-forward audit entry is written with `details.taskId="T-001"` and `details.assignee="alice"`. Granted outcome.

## 6. Key Entities and Business Rules

| Entity | Field | Rule |
|--------|-------|------|
| `CheckpointContract` | `per_task` (new, bool, default `false`) | When `true`, this checkpoint pauses once per task; pending actions are keyed by `(work_item_id, checkpoint_key, task_id)`. When `false`, pre-FEAT semantics. |
| `PendingAction` | `task_id` (new, nullable string) | Present iff the contract has `per_task=true`. Part of the active-row uniqueness. |
| `PendingAction` | uniqueness | Active rows unique on `(work_item_id, checkpoint_key, COALESCE(task_id, '<root>'))`. Closed rows unconstrained. |
| Signal payload | `taskId` | Optional on the wire (the executor defaults to current task); DevHub forwards verbatim when set. |
| Signal payload | `assignee` | Required non-empty for `assignment-confirmed` specifically. DevHub validates at the boundary. |
| Loop-back | Per-task identity | Closing the T-N row does NOT block opening a T-(N+1) row with the same `checkpoint_key`. The discriminator is `task_id`, not `checkpoint_key`. |

## 7. API Impact

- `POST /api/projects/{pid}/work-items/{wid}/signal` (existing) gains an optional `taskId` in the request body, forwarded verbatim to the executor's signal endpoint.
- `PendingActionDto` (in SSE stream + REST list) gains a nullable `taskId` field.
- `CheckpointContractDto` (admin executor endpoints) gains a `perTask` boolean.
- No new endpoints.

## 8. UI Impact

- **Lifecycle-screen page** (existing): when the active checkpoint is `assignment-confirmed`, the panel renders a member-picker (scoped to project memberships) plus a free-text "Other" option. Submit button is disabled until a value is chosen. Cancel routes back to the dashboard.
- **Operator dashboard "Pending on you" panel**: per-task pauses render as their own rows, labeled with `<work item title> — <task id>`.
- **Sidebar badge**: numeric count includes per-task pending actions.
- **Work item detail page**: new "Assignments" sidebar lists task→assignee from fetched executor state.

## 9. Edge Cases

- **Executor sends `assignment-confirmed` raised with `taskId=null`** despite `perTask=true`. DevHub still creates the row, with `task_id=NULL`. Discriminator becomes the literal sentinel `'<root>'` for uniqueness. Logged as a warning since the contract-shape and event-shape disagree.
- **Operator submits `assignment-confirmed` with no `taskId`** while a per-task contract is active. DevHub forwards without `taskId` and lets the executor default to its `current_task_id`. The corresponding pending action row is matched by `taskId=NULL` or by the most-recently-raised non-NULL row in that work item — we pick the latter to minimize operator confusion.
- **Two `PendingActionRaised` events with the same `taskId` arrive in quick succession** (e.g., network blip / replay). The first creates the row; the second is a no-op (active row already exists for that key). No double-surfacing in the dashboard.
- **Operator changes their mind before submitting.** Closing the panel without submitting leaves the pending action open — same behavior as every other checkpoint.
- **Member with `displayName` containing characters the executor's idempotency hash treats specially.** `assignee` is opaque to DevHub; we forward bytes. The executor's idempotency key is `(run_id, "assignment-confirmed", task_id)` — assignee is not part of that key, so even a quoted-with-special-chars assignee will not cause dedupe collisions.
- **A non-manual variant work item arrives at a checkpoint named `assignment-confirmed`.** Should not happen per the spec (the contract is only registered on manual-variant executors), but if it does the contract's `perTask` flag drives the row-keying — graceful degradation.

## 10. Constraints

- **No DevHub-side persistence of `assignee`** beyond the audit log and the in-flight signal. The source of truth is `RunMemory.data.assignments` on the executor. This keeps DevHub from drifting out of sync with the executor's memory.
- **Member-picker is convenience, not enforcement.** The executor sees only the string. DevHub does not enforce that `assignee` corresponds to a real DevHub member — the free-text escape hatch is deliberate.
- **Per-task checkpoints are a generalization, not a special case.** The schema change (`per_task` column, `task_id` column) is the abstraction; `assignment-confirmed` is the first user. Other checkpoints can opt in later without further migration.
- **Idempotency stays executor-side.** DevHub forwards every submit. Don't add a DevHub-side dedupe.

## 11. Motivation and Priority Justification

**Motivation:** The orchestrator's `lifecycle-agent@0.4.0-manual` flow now has a fifth checkpoint that fires per task. Without this FEAT, DevHub can't drive the manual variant past the first task: the loop-back from `mark_task_done` would surface a new pending action that DevHub interprets as a duplicate of the previous task's, and the operator has no UI to deliver a `payload.assignee`. The whole manual-variant onboarding path is gated on this.

**Impact if delayed:** The manual variant becomes effectively unusable from DevHub for any multi-task work item. Operators would have to deliver signals directly against the orchestrator's `POST /api/v1/runs/{id}/signals` endpoint — exactly the front-door bypass the stakeholder definition prohibits.

**Dependencies:** Independent of FEAT-008 (code-source binding). Both can ship in parallel. FEAT-008 has the harder deadline (orchestrator flag flip); FEAT-009 has the bigger UX surface.

## 12. Traceability

| Reference | Link |
|-----------|------|
| **Persona** | Operator |
| **Stakeholder Scope Item** | Front-door discipline — every operator decision flows through DevHub, no executor-side back-channels |
| **Success Metric** | Manual-variant multi-task work item completes end-to-end through DevHub UI |
| **Related Work Items** | Independent of FEAT-008. Builds on FEAT-005 (notifications / pending actions) and FEAT-006 (operator dashboard). |
| **Upstream spec** | carestechs-agent-orchestrator IMP-005 release notes (2026-05-17): `assignment-confirmed` signal, per-task loop-back via `confirm_assignment`, `RunMemory.data.assignments` sidecar |
| **Generalization opportunity** | `per_task` flag on `CheckpointContract` is the seam for any future per-task pauses on other executors |
