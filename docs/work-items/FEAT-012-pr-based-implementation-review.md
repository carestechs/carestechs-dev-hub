# Feature Brief: FEAT-012 — PR-Based Implementation Review

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-012 |
| **Name** | PR-Based Implementation Review |
| **Target Version** | v1 |
| **Status** | Open |
| **Priority** | High |
| **Requested By** | Operator (reduce friction at the implementation-complete checkpoint) |
| **Date Created** | 2026-06-29 |

## 2. User Story

**As a** developer who has finished implementing a task, **I want to** open a PR and submit its URL directly from the `implementation-complete` review screen, **so that** the agent's self-review runs against the actual PR diff — not a raw branch — and I can re-submit the same PR URL after pushing fixes without changing anything else in the flow.

## 3. Goal

Replace the current blank `implementation-complete` signal (a formality with no data) with a purposeful PR submission screen. The developer pastes a PR URL (or corrects the pre-populated one), hits "Ready for Review", and the signal carries that URL as payload. The orchestrator receives it and passes it to the agent self-reviewer as context. If the review fails and the developer pushes fixes, they return to the same screen, confirm the same PR URL, and re-trigger the review — no branch juggling required.

## 4. Feature Scope

### 4.1 Included

#### Frontend — `ImplementationReviewPanel` upgrade

- **PR URL input field.** Editable text input, pre-populated from `workItem.workBranch` when it looks like a PR URL; otherwise blank. Validated as a non-empty string before submit is enabled.
- **Branch display.** Show the current `workBranch` as read-only context below the PR URL field so the developer can verify they're pointing at the right branch.
- **"Ready for Review" submit button.** Replaces the current `onComplete` button. Sends signal with `outcome: "approve"` and `payload: { prUrl: "<url>" }`.
- **"Request Changes" path unchanged.** The reject path (`verdict: "reject"` + feedback) stays as-is — it handles the case where the operator (not the dev) blocks the signal before it reaches the orchestrator.
- **Re-submission UX.** When the work item returns to `implementation-complete` after a failed agent review, the panel re-displays with the previously submitted PR URL pre-populated from the step's `nodeInputs`. Developer can confirm as-is or update.

#### Backend — persist `prUrl` on the work item

- **New `WorkItem.PrUrl` column** (`text`, nullable). Populated from the `implementation-complete` signal payload on every submission (including re-submissions). Exposed on `WorkItemDto` and `WorkItemSummaryDto` as `prUrl: string | null`.
- **Migration** added to `DevHub.Modules.WorkItems`.
- **`WorkItemsService.SignalAsync`** — after forwarding the signal to the executor, extract `payload.prUrl` and persist it on the `WorkItem` row within the same transaction.
- **Audit detail** — include `prUrl` in the `workitem:signal` audit entry when present.

#### Frontend — review page

- The review page already re-displays the `ImplementationReviewPanel` when `currentCheckpointKey === "implementation-complete"`. No routing changes needed.
- `WorkItemDto` projection already flows through `reviewPage`; add `prUrl` display to the panel's read-only state section.

### 4.2 Excluded

- **CI/webhook automation** — auto-signaling on PR open/push is a follow-up. This FEAT establishes the manual path; automation layers on top.
- **PR metadata fetch** — fetching PR title, status, or diff from GitHub to display inline is out of scope. The URL is passed to the orchestrator as-is; the agent fetches what it needs.
- **PR URL validation against the project's `repo` field** — a nice-to-have but deferred. Any non-empty URL is accepted.
- **`workBranch` auto-update** — if the dev's PR URL implies a different branch than `workItem.workBranch`, we do not automatically update `workBranch`. That stays a separate PATCH operation.

## 5. Acceptance Criteria

- **AC-1:** `ImplementationReviewPanel` shows an editable PR URL field and a "Ready for Review" button; submit is disabled when the field is empty.
- **AC-2:** Signal payload sent to the orchestrator contains `{ prUrl: "<url>" }`.
- **AC-3:** `WorkItem.prUrl` is persisted after a successful `implementation-complete` signal and returned in `WorkItemDto`.
- **AC-4:** When the work item cycles back to `implementation-complete` (after a failed review), the panel pre-populates the PR URL from the previous submission.
- **AC-5:** Existing reject ("Request Changes") path continues to work without modification.
- **AC-6:** `prUrl` appears in the audit entry for the `workitem:signal` action.

## 6. Key Entities and Business Rules

| Entity | Change | Rule |
|--------|--------|------|
| `WorkItem` | New `PrUrl` column | Nullable; updated on every `implementation-complete` signal that carries `prUrl` in payload |
| `WorkItemDto` | New `prUrl` field | Null when never submitted or when cleared |
| Signal payload | `prUrl` field | Forwarded verbatim to orchestrator; DevHub does not validate URL format beyond non-empty |

## 7. API Impact

| Endpoint | Change |
|----------|--------|
| `POST .../checkpoints/implementation-complete/signal` | Payload may now carry `prUrl: string`; DevHub extracts and persists it |
| `GET .../work-items/{id}` | `WorkItemDto.prUrl` added (nullable) |
| `GET .../work-items` | `WorkItemSummaryDto.prUrl` added (nullable) |

## 8. UI Impact

| Screen / Component | Change |
|--------------------|--------|
| `ImplementationReviewPanel` | Add PR URL input, branch display, update submit handler |
| Review page | No routing change; `prUrl` surfaced in work item context |

## 9. Dependencies

| Dependency | Direction |
|------------|-----------|
| FEAT-011 — First-Class Lifecycle Screens | Must be complete; `ImplementationReviewPanel` exists and is wired |
| FEAT-016 (orchestrator) — PR URL in agent self-review context | Parallel; DevHub side can ship independently — orchestrator ignores unknown payload fields until FEAT-016 lands |

## 10. Motivation and Priority Justification

The current `implementation-complete` gate is a blank signal — the developer goes to DevHub, finds the work item, and clicks approve with no data attached. The agent self-reviewer then has no reference to what was actually implemented. This makes the review shallow and the gate meaningless. Attaching a PR URL turns the gate into a real handoff: the developer declares what they built, and the agent reviews exactly that.
