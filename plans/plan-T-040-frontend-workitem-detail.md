# Implementation Plan: T-040 — Work-item detail page (generic, executor-agnostic)

## Task Reference
- **Task ID:** T-040 · **Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M
- **Rationale:** Every executor we register gets a working detail page for free. UI Spec §5.

## Overview
`/projects/:slug/work-items/:id` — generic surface that renders `WorkItemHeader`, `ExecutorStatePanel` (opaque key-value), `SignalHistoryList`, `StreamFeed` (SSE). Cancel button when role-gated. "Open review →" CTA when the executor key matches the feature-delivery allowlist.

## Implementation Steps

### Step 0: Mockup
**File:** `mockups/work-item-detail.html` · Create
Per UI Spec §5 ascii — header with title/status/executor chip + "Open review" CTA, executor state key-value list, stream feed (mocked tail of events), signal history list, cancel button.

### Step 1: Components
**Files (Create):**
- `client/src/app/features/projects/work-items/components/executor-state-panel.{ts,html}` — recursive key-value renderer. Nested objects collapsible. Primitive leaves rendered as `<key>: <value>`.
- `client/src/app/features/projects/work-items/components/signal-history-list.{ts,html}` — list of `CheckpointSignalDto` with member, outcome pill, signaled-at. "Load more" appends via `WorkItemsService.listSignals(...)`.
- `client/src/app/features/projects/work-items/components/stream-feed.{ts,html}` — opens `EventSource(work-items.streamUrl(projectId, workItemId, accessToken))` on mount, appends events to a signal-backed list, closes on destroy. Reconnect button shown on `EventSource.onerror`. `aria-live="polite"`.

### Step 2: Detail page
**File:** `client/src/app/features/projects/work-items/work-item-detail.page.{ts,html,spec.ts}` · Create

Reads `:slug` + `:id` from `ActivatedRoute`. Fires `getProjectBySlug` + `WorkItemsService.get` + `WorkItemsService.listSignals` in parallel. Provides the work item, signals, and stream URL to its children. Cancel button is conditional on `caller.hasRole(cancelContract.requiredRoleKey)` — fall back to "operator only" if no cancel contract. "Open review" CTA visible when `workItem.executor.key === 'feature-delivery-v1'`.

403 → friendly forbidden page. 404 → "Work item not found" with link to `/projects/:slug`.

### Step 3: Access token plumbing for SSE
**File:** `client/src/app/core/auth/auth.service.ts` · Modify (read only)
Expose `accessToken()` as a signal so `StreamFeed` can compose the URL. Existing implementation already stores it in memory — just publicize via a getter.

### Step 4: Route
**File:** `client/src/app/app.routes.ts` · Modify
Add:
```ts
{
  path: 'projects/:slug/work-items/:id',
  loadComponent: () => import('./features/projects/work-items/work-item-detail.page').then(m => m.WorkItemDetailPage),
}
```

### Step 5: Specs
- `work-item-detail.page.spec.ts` — parallel load renders header + state + signals; 403 → forbidden page; 404 → not-found page; cancel button shown when caller has role.
- `executor-state-panel.spec.ts` — renders primitives, nests collapsibly.
- `signal-history-list.spec.ts` — renders rows, "Load more" appends.
- `stream-feed.spec.ts` — uses a fake `EventSource` (jasmine spy via `globalThis.EventSource = ...`) to assert events get appended; reconnect button on error.

## Files Affected
| File | Action |
|------|--------|
| `mockups/work-item-detail.html` | Create |
| `features/projects/work-items/components/{executor-state-panel,signal-history-list,stream-feed}.*` | Create |
| `features/projects/work-items/work-item-detail.page.{ts,html,spec.ts}` | Create |
| `app.routes.ts` | Modify |
| `core/auth/auth.service.ts` | Modify (expose accessToken) |

## Edge Cases & Risks
- **EventSource auth** — the `?access_token=` query workaround means the token shows up in browser history. Mitigate by configuring the auth service to never include the SSE URL in the history; for v1 we ship the simple shape and document the trade-off.
- **EventSource reconnect** — by default, EventSource auto-reconnects with a 3s delay. We do NOT disable that. The reconnect button is for user-initiated re-open after a fatal error (e.g., 4xx).
- **`executorState` shape** — opaque. The panel renders whatever it gets. If it's `null`/empty, show a friendly "No state reported yet."
- **Cancel button visibility** — without a `cancel` contract, the button shows for operators only. With one, it shows for callers who have the contract's `requiredRoleKey`. v1 ships the cancel contract as optional.

## Acceptance Verification
- [ ] Mockup approved.
- [ ] `ng build` clean.
- [ ] `ng test` is green; new spec count ≥ 6.
- [ ] Manual smoke: open a work item detail page, observe the executor state, watch a stream tick by (fake executor scripted to emit 3 chunks).
