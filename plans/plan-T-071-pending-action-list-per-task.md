# Implementation Plan: T-071 — Per-task labeling on pending-action rows

## Task Reference
- **Task ID:** T-071 · **Type:** Frontend · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-6, AC-7. Disambiguate multi-task pending rows in the dashboard + home page.

## Overview
Touch one shared component (`pending-action-list`). Row label suffixes `— <task id>` when present. Click handler appends `?taskId=` to the route. Sidebar badge unchanged (it counts list length, which already discriminates per-task).

## Implementation Steps

### Step 1: Update the template
**File:** `client/src/app/features/home/pending-action-list.html` · Modify

Find the row label rendering (the `<a>` or similar wrapping the title). Append:

```html
<span class="font-medium">{{ row.workItemTitle }}</span>
@if (row.taskId; as t) {
  <span class="text-slate-500"> — <span class="font-mono text-xs">{{ t }}</span></span>
}
```

### Step 2: Update the click handler / routerLink
**File:** `client/src/app/features/home/pending-action-list.ts` · Modify

If the existing click handler builds a route array, conditionally include `queryParams: { taskId: row.taskId }`:

```ts
protected linkFor(row: PendingActionDto): { commands: any[]; queryParams?: Record<string, string> } {
  const commands = ['/projects', row.projectSlug, 'work-items', row.workItemId, 'review'];
  return row.taskId ? { commands, queryParams: { taskId: row.taskId } } : { commands };
}
```

If the template uses `[routerLink]` directly, switch to `[routerLink]="linkFor(row).commands"` and `[queryParams]="linkFor(row).queryParams"`.

### Step 3: Specs
**File:** `client/src/app/features/home/pending-action-list.spec.ts` · Modify

Add specs:

1. **Single-task row** with `taskId: null` renders just the title — no `—` separator.
2. **Per-task row** with `taskId: 'T-001'` renders `Title — T-001` and the routerLink carries `queryParams: { taskId: 'T-001' }`.
3. **Two rows for the same work item** with different `taskId`s render two distinct labels (loop-back scenario).

### Step 4: Operator dashboard usage check
**File:** `client/src/app/features/operator/operator-dashboard.page.{ts,html}` · Verify

If the dashboard's pending-actions panel reuses `<pending-action-list>` directly, no change needed. If it has its own row rendering, port the same label + queryParams logic. Spec coverage on the dashboard is desirable but only mandatory if the rendering is local.

### Step 5: Build + test
**Bash:**

```bash
cd client && npx ng build --configuration development
npx ng test --watch=false --browsers=ChromeHeadless
```

All existing tests stay green; +3 from the new specs.

## Files Affected
| File | Action |
|------|--------|
| `client/src/app/features/home/pending-action-list.ts` + `.html` | Modify |
| `client/src/app/features/home/pending-action-list.spec.ts` | Modify |
| `client/src/app/features/operator/operator-dashboard.page.*` | Verify, modify if local rendering |

## Edge Cases & Risks
- **Long task IDs** (the orchestrator uses `T-001` style — short and stable). If a future executor uses longer ids, truncate with `text-ellipsis overflow-hidden` if needed. v1 doesn't need it.
- **The review page consuming `?taskId`** is informational only — T-070's panel reads `workItem.currentTaskId`, not the URL param. The query param is just for human-readability and back-button friendliness.

## Acceptance Verification
- [ ] Per-task rows render with `— <task id>` suffix; non-task rows unchanged.
- [ ] Click navigates with `?taskId=` query param when set.
- [ ] Specs cover all three scenarios.
- [ ] Badge count auto-includes per-task rows (no logic change).
