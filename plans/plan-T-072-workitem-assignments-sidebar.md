# Implementation Plan: T-072 — WorkItem detail Assignments sidebar

## Task Reference
- **Task ID:** T-072 · **Type:** Frontend · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-9. Surface the orchestrator's `RunMemory.data.assignments` sidecar without persisting it in DevHub.

## Overview
A small read-through section on the WorkItem detail page. Reads `workItem.executorState.assignments` (a `Record<taskId, assignee>` map). Renders one row per pair, sorted by `taskId`. No DevHub-side persistence.

## Implementation Steps

### Step 1: Add a computed for assignments
**File:** `client/src/app/features/projects/work-items/work-item-detail.page.ts` · Modify

```ts
protected readonly assignments = computed<Array<[string, string]>>(() => {
  const state = this.workItem()?.executorState as unknown;
  if (!state || typeof state !== 'object') return [];
  const a = (state as Record<string, unknown>)['assignments'];
  if (!a || typeof a !== 'object') return [];
  return Object.entries(a as Record<string, unknown>)
    .filter(([, v]) => typeof v === 'string' && (v as string).length > 0)
    .map(([k, v]) => [k, v as string] as [string, string])
    .sort(([a], [b]) => a.localeCompare(b));
});
```

The narrow shape check keeps the page from crashing on malformed `executorState`.

### Step 2: Render the section
**File:** `client/src/app/features/projects/work-items/work-item-detail.page.html` · Modify

Place after the Branch row (or wherever sidebar-like content goes — match the existing detail-page layout). Conditional on the assignments list being non-empty:

```html
@if (assignments(); as a) {
  @if (a.length > 0) {
    <section class="mt-6 bg-white rounded-xl shadow-sm p-6">
      <h2 class="text-lg font-semibold mb-3">Assignments</h2>
      <ul class="space-y-1 text-sm">
        @for (entry of a; track entry[0]) {
          <li class="flex items-center gap-3">
            <span class="font-mono text-xs bg-slate-100 rounded px-1.5 py-0.5">{{ entry[0] }}</span>
            <span>→</span>
            <span>{{ entry[1] }}</span>
          </li>
        }
      </ul>
    </section>
  }
}
```

### Step 3: Specs
**File:** `client/src/app/features/projects/work-items/work-item-detail.page.spec.ts` · Modify

Two new specs (the existing 13 stay green):

1. **Renders assignments when present.** Flush a work item with `executorState: { assignments: { 'T-002': 'Bob', 'T-001': 'Alice' } }` → expect rendered DOM contains both pairs, in sorted order (T-001 before T-002).
2. **No section when absent.** Flush with `executorState: {}` → no "Assignments" heading in the DOM.

### Step 4: Build + test
**Bash:**

```bash
cd client && npx ng build --configuration development
npx ng test --watch=false --browsers=ChromeHeadless
```

### Step 5: Update `docs/ui-specification.md`
**File:** `docs/ui-specification.md` · Modify

WorkItemDetailPage section: append an "Assignments" panel description. Changelog entry:

```
| 2026-05-17 (FEAT-009 / T-072) | WorkItem detail page gained an "Assignments" panel — read-through of executorState.assignments (taskId → assignee). No DevHub-side persistence. |
```

## Files Affected
| File | Action |
|------|--------|
| `client/src/app/features/projects/work-items/work-item-detail.page.ts` + `.html` + `.spec.ts` | Modify |
| `docs/ui-specification.md` | Modify |

## Edge Cases & Risks
- **Malformed `executorState`.** The narrow shape check (`typeof === 'object'`) + per-entry string filter returns an empty array on anything unexpected. The section just doesn't render.
- **Renamed members.** If a member's `displayName` changes after the assignment was made, the sidebar shows the value at-time-of-assignment (because it's the executor's record, not a DevHub join). Documented as v1 behavior in the brief.
- **Stale state.** The page re-fetches the work item on transition events (existing `onCheckpointResolved` handler). Assignments refresh naturally.

## Acceptance Verification
- [ ] Section renders only when the assignments map is non-empty.
- [ ] Rows sorted ascending by taskId.
- [ ] Two specs cover the present + absent cases.
- [ ] `docs/ui-specification.md` updated.
