# Implementation Plan: T-041 — Lifecycle review (feature-delivery) screen

## Task Reference
- **Task ID:** T-041 · **Type:** Frontend · **Workflow:** mockup-first · **Complexity:** L
- **Rationale:** The "lifecycle-aware screen" the Stakeholder Definition commits to. Without it, v1 doesn't ship its required affordance.

## Overview
`/projects/:slug/work-items/:id/review` — the feature-delivery-shaped review surface. Timeline + active artefact + decision history + checkpoint action bar + live stream. UI Spec §6.

## Implementation Steps

### Step 0: Mockup
**File:** `mockups/work-item-review.html` · Create
Per UI Spec §6 ascii. Variants in the file: default (action bar enabled), read-only (lacks role), no-active-checkpoint, submitting, submit-failure.

### Step 1: Components
**Files (Create):**
- `client/src/app/features/projects/work-items/components/lifecycle-timeline.{ts,html}` — ordered steps with state badges (`approved` / `active` / `pending`). Input: `steps: { key, label, state, signal? }[]`. Click a past step → emits `stepSelected`.
- `client/src/app/features/projects/work-items/components/active-step-artefact-panel.{ts,html}` — shape-detection: if `artefact.kind === 'diff'` render `DiffViewer`; `'markdown'` render `MarkdownRenderer`; else `ArtefactFallback`.
- `client/src/app/features/projects/work-items/components/diff-viewer.{ts,html}` — accepts `{ added: string[], removed: string[], context: string[] }`-shaped lines; renders red/green/slate with monospace.
- `client/src/app/features/projects/work-items/components/markdown-renderer.{ts,html}` — minimal: headings, lists, paragraphs, code blocks. No images/links in v1.
- `client/src/app/features/projects/work-items/components/artefact-fallback.{ts,html}` — pretty-prints the JSON.
- `client/src/app/features/projects/work-items/components/decision-history-list.{ts,html}` — one row per past signal; member + outcome + signaled-at + payload-notes excerpt.
- `client/src/app/features/projects/work-items/components/checkpoint-action-bar.{ts,html}` — one button per `allowedOutcome` + free-form notes textarea. Submit emits `{ outcome, payload: { notes } }`.

### Step 2: Review page
**File:** `client/src/app/features/projects/work-items/review.page.{ts,html,spec.ts}` · Create

Loads `WorkItem`, signals, and (when current checkpoint exists) the contract via three parallel requests. Builds the timeline from `executorState.steps` + signals (feature-delivery convention: `steps: [{ key, label, signal? }, ...]`). Picks the active step (`signal == null` first in order; else `null` = nothing active).

Role-gating for the action bar:
- The page reads the caller's roles on this project from the JWT (cached in `AuthService.memberships()`).
- `requiredRole = contract.requiredRoleKey` for the active step.
- `caller.hasRole = isOperator || memberships[projectId]?.includes(requiredRole)`.
- If `!caller.hasRole`, show the action bar in `disabled` mode with caption "This step is waiting on role: <requiredRole>."

Submit:
- Generates a per-attempt `Idempotency-Key` (`crypto.randomUUID()`).
- `await WorkItemsService.signal(projectId, workItemId, key, { outcome, payload: { notes } }, idempotencyKey)`.
- On success: refetch work item + signals; if `currentCheckpointKey` advanced, the timeline naturally re-renders with the new active step.
- On 400 (outcome-mismatch) / 502 (executor failure): inline `AppErrorBanner` above the bar. 502 banner shows `executorKey` + `correlationId` with a copy button.

Live SSE: reuse `StreamFeed`. When an event arrives, debounce 250ms then refetch the work item (cheap; cache-only DTOs).

### Step 3: Route + CTA wiring
**File:** `client/src/app/app.routes.ts` · Modify
```ts
{
  path: 'projects/:slug/work-items/:id/review',
  loadComponent: () => import('./features/projects/work-items/review.page').then(m => m.ReviewPage),
}
```

**File:** `client/src/app/features/projects/work-items/work-item-detail.page.ts` (from T-040) · Verify
The "Open review" CTA renders the link to the review route when `workItem.executor.key === 'feature-delivery-v1'`.

### Step 4: Specs
- `review.page.spec.ts` — timeline renders ordered steps from `executorState.steps`; action bar role-gated (enabled vs disabled); submit happy path refetches + advances; 400 surfaces inline; 502 shows executor key + correlation id with copy button.
- `lifecycle-timeline.spec.ts` — renders states correctly, emits `stepSelected` on past-step click.
- `checkpoint-action-bar.spec.ts` — renders one button per `allowedOutcome`, submit emits the correct payload.
- `diff-viewer.spec.ts` — renders added/removed/context lines with the right classes.
- `markdown-renderer.spec.ts` — covers headings, lists, code blocks, paragraphs.

## Files Affected
| File | Action |
|------|--------|
| `mockups/work-item-review.html` | Create |
| `features/projects/work-items/components/{lifecycle-timeline,active-step-artefact-panel,diff-viewer,markdown-renderer,artefact-fallback,decision-history-list,checkpoint-action-bar}.*` | Create |
| `features/projects/work-items/review.page.{ts,html,spec.ts}` | Create |
| `app.routes.ts` | Modify |
| `features/projects/work-items/work-item-detail.page.ts` | Verify |

## Edge Cases & Risks
- **Shape coupling to feature-delivery.** The review page assumes `executorState.steps: [{ key, label, signal? }]`. If the feature-delivery executor's shape evolves, we update this page. Document on `LifecycleTimeline` and on the page comment.
- **No active step.** If status is `Running` / `Completed` / `Cancelled`, hide the action bar and show a banner reflecting status.
- **Markdown renderer scope.** Headings + lists + paragraphs + code blocks only. Document the v1 cap; explicit fallback for unsupported syntax.
- **Idempotency keys.** Each *attempt* gets a fresh UUID — double-clicking submit reuses the in-flight key (preventing duplicate forwards). A separate retry after a 502 uses a fresh key (the original signal landed but the executor failed; the user is intentionally retrying).
- **SSE refetch storms.** Debounce 250ms on stream events to avoid hitting the API on every chunk.

## Acceptance Verification
- [ ] Mockup approved (5 variants).
- [ ] `ng build` clean.
- [ ] `ng test` is green; new spec count ≥ 8.
- [ ] Manual smoke: with the fake executor scripting a feature-delivery-shaped state, navigate to the review page, click an outcome, observe the timeline advance.

## FEAT-004 completion gate

After T-041 merges:
- Backend façade covers start / get / signal / signals / cancel / stream with full audit.
- Frontend ships Project home work-items table + detail + lifecycle review.
- 71+ backend tests + 114+ SPA tests pass.
- Stakeholder definition: "at least one lifecycle-aware screen" satisfied.
- FEAT-005 (notifications) and FEAT-006 (operator dashboard) unblocked.
