# Feature Brief: FEAT-005 — Pending-Action Notifications

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-005 |
| **Name** | Pending-Action Notifications (in-app) |
| **Target Version** | v1 |
| **Status** | Not Started |
| **Priority** | High |
| **Requested By** | Stakeholder ("a member must discover pending action without polling") |
| **Date Created** | 2026-05-15 |

## 2. User Story

**As a** project member, **I want to** see — without polling — every checkpoint waiting on my role across every project I belong to, **so that** I can act as soon as the work needs me.

## 3. Goal

A live "Pending on you" inbox in the SPA sidebar and on Home, kept current by a server-pushed stream. v1 channel is in-app only (deferred channels: email, webhook).

## 4. Feature Scope

### 4.1 Included

- Entity: PendingActionSignal.
- Endpoints: `GET /api/notifications/pending`, `GET /api/notifications/stream` (SSE).
- A `NotificationsService` that listens to WorkItem state transitions (in-process domain events) and reconciles `PendingActionSignal` rows.
- UI: live PendingActionList on Home, live group in the sidebar (count badge in the header).

### 4.2 Excluded

- Email / webhook channels (deferred — open question in stakeholder def).
- Snoozing or muting specific projects.
- "Pending on my team" (only "pending on me" in v1).

## 5. Acceptance Criteria

- **AC-1:** Within ≤2s of a `WaitingOnCheckpoint` transition on the executor, the responsible members see a new entry in their PendingActionList (verified with a controlled test executor + clock).
- **AC-2:** Resolving the checkpoint (any outcome) dismisses the entry for every responsible member.
- **AC-3:** The sidebar count badge stays in sync; closing/reopening the tab restores the correct count from `GET /api/notifications/pending`.
- **AC-4:** ≥80% of checkpoints in a smoke run are acted on by the responsible member (Success Metric: Operator self-service ratio).
- **AC-5:** Disconnecting and reconnecting the SSE stream does not produce duplicate entries.

## 6. Key Entities and Business Rules

| Entity | Role | Rules |
|--------|------|-------|
| PendingActionSignal | Live "waiting on you" entry | Unique per `(member_id, work_item_id, checkpoint_key)` while not dismissed |

## 7. API Impact

`GET /api/notifications/pending`, `GET /api/notifications/stream` (see `api-spec.md`).

## 8. UI Impact

| Screen | Status | Description |
|--------|--------|-------------|
| Home (PendingActionList) | New | Live list; SSE updates |
| Sidebar live group + header badge | New | Persistent across screens |

## 9. Edge Cases

- Member loses required role before acting → entry vanishes on the next reconciliation tick (and 403 on submit if they raced).
- Member is added to a project that already has pending checkpoints for their role → entries are backfilled on next reconcile.
- Stream-feed gap (server restart) → on reconnect, client refetches `/pending` to resync.

## 10. Constraints

- Pass-through SSE (no buffering, no batching beyond the natural event boundary).
- No per-channel logic in v1 — the schema is channel-agnostic, but only the in-app channel is wired.

## 11. Motivation and Priority Justification

**Motivation:** Without this, members must hunt for pending work — exactly the pain the portfolio is supposed to remove.
**Impact if delayed:** Operator dependence persists; Success Metric "Operator self-service ratio" stays low.
**Dependencies on this feature:** None block on this, but the operator dashboard (FEAT-006) is materially better with notifications in place.

## 12. Traceability

| Reference | Link |
|-----------|------|
| **Persona** | `docs/personas/primary-user.md` (Peak Pain Moment) |
| **Stakeholder Scope Item** | "Notification surface for pending action" |
| **Success Metric** | "Operator self-service ratio ≥80%" |
| **Related Work Items** | Blocked by FEAT-004. |
