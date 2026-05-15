# Feature Brief: FEAT-004 — Work Items Façade (start, checkpoint, fetch, stream)

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-004 |
| **Name** | Work Items Façade — start, checkpoint signal, fetch state, live stream |
| **Target Version** | v1 |
| **Status** | Not Started |
| **Priority** | Critical |
| **Requested By** | Architecture (Stakeholder Scope: Generic facade surface) |
| **Date Created** | 2026-05-15 |

## 2. User Story

**As a** project member, **I want to** start a work item, fetch its current state, send a checkpoint signal, and watch the live trace, **so that** I can advance work without ever knowing which executor is running it.

## 3. Goal

The portfolio-mediated entry points to executors, fully authorized at the boundary, fully audited, and streaming pass-through. Satisfies Success Criteria #2 ("no end-user action requires reaching a lifecycle executor directly") and #4 ("authorization is end-to-end").

## 4. Feature Scope

### 4.1 Included

- Entities: WorkItem, CheckpointSignal.
- All endpoints under `/api/projects/{id}/work-items/*` (see `api-spec.md`).
- SSE pass-through for `/stream` — no buffering, no transformation.
- `(member, role, project, target)` authorization check before every forward, consulting `CheckpointContract.requiredRoleKey` for signals.
- Audit entry on grant AND deny AND executor failure for every endpoint.
- UI: Work-item table on Project home; Work-item detail; Lifecycle review (feature-delivery).

### 4.2 Excluded

- Cross-project work items (explicitly out of stakeholder scope).
- Replay of past stream events (executor is authoritative).
- Background polling fallback if SSE fails — UI shows reconnect button only.

## 5. Acceptance Criteria

- **AC-1:** A member without the required role for a checkpoint receives 403 and the action **never reaches the executor** (verified by absence of any outbound HTTP call in the test double).
- **AC-2:** P95 added latency by the portfolio over a direct executor call on `/stream` and `GET /work-items/{id}` is within an order of magnitude of the executor's own P95.
- **AC-3:** Every audit entry for a Granted action carries the portfolio-issued `executorCorrelationMarker` (matches Success Metric "Front-door discipline").
- **AC-4:** SSE bytes from the executor reach the browser without buffering — verified by streaming a controlled byte sequence from a test executor and observing chunk-by-chunk arrival.
- **AC-5:** Cancellation requires the role declared by the cancel checkpoint contract (or `System:operator`).
- **AC-6:** A signal with an outcome not in `allowedOutcomes` returns 400 before forward.

## 6. Key Entities and Business Rules

| Entity | Role | Rules |
|--------|------|-------|
| WorkItem | Project-scoped index entry | `current_status` is a cache; executor is authoritative |
| CheckpointSignal | Forwarded action record | Captured **before** forward; updated with executor response |

## 7. API Impact

All `/api/projects/{id}/work-items/*` endpoints in `api-spec.md` § Work Items.

## 8. UI Impact

| Screen | Status | Description |
|--------|--------|-------------|
| Project home (work-item table portion) | New (extends FEAT-002 stub) | List + start work modal |
| Work-item detail | New | Generic header, executor state, signal history, stream feed |
| Lifecycle review (feature-delivery) | New | Timeline, artefact, decision history, checkpoint action bar |

## 9. Edge Cases

- Executor unreachable → 502 with `correlationId` and executor key; audited as Failed; no partial state changes (CheckpointSignal row still recorded with `executor_response_status = null`).
- Checkpoint already resolved when signal arrives → 409.
- Member loses project membership between page load and submit → 403 at submit time.
- Stream client disconnects mid-stream → upstream connection closed; no replay state retained.
- Same member double-clicks Approve → idempotency on the signal endpoint (client supplies `idempotencyKey` header; server stores `(workItemId, idempotencyKey)` unique).

## 10. Constraints

- Streaming is hot path — **no** buffering, batching, or transformation, ever.
- All forwards use `IExecutorRouter` + `CheckpointContract`; no hard-coded executor URLs anywhere.
- Authorization MUST be the first non-validation line of every controller action.
- Every façade endpoint requires a deny-path test in PR review.

## 11. Motivation and Priority Justification

**Motivation:** This is the feature that the portfolio exists for. Everything else is in service of this surface.
**Impact if delayed:** No end users can act; v1 has nothing to demo.
**Dependencies on this feature:** FEAT-005, FEAT-006.

## 12. Traceability

| Reference | Link |
|-----------|------|
| **Persona** | `docs/personas/primary-user.md` |
| **Stakeholder Scope Item** | "Generic facade surface"; "At least one lifecycle-aware screen" |
| **Success Metric** | "Authorization correctness 100%"; "Facade transparency"; "Front-door discipline" |
| **Related Work Items** | Blocked by FEAT-001, FEAT-002, FEAT-003. Blocks FEAT-005, FEAT-006. |
