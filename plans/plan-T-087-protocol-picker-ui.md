# Implementation Plan: T-087 — Operator UI: protocol picker on executor admin

## Task Reference
- **Task ID:** T-087 · **Type:** Frontend · **Workflow:** standard · **Complexity:** S
- **Rationale:** Without UI, operators can't flip executors to the orchestrator protocol without writing to the DB.

## Overview
Two small surfaces: the executors admin create/edit modal gains a Protocol dropdown; the WorkItem detail header shows `executorRunId` next to the existing `marker` label.

## Implementation Steps

### Step 1: Extend the executor types
**File:** `client/src/app/core/api/executor-registry.types.ts` · Modify

```ts
export type ExecutorProtocol = 'devhub' | 'orchestrator';

export interface ExecutorDto {
  // …existing fields…
  protocol: ExecutorProtocol;
}

export interface CreateExecutorRequest {
  // …existing fields…
  protocol?: ExecutorProtocol;  // server defaults to 'devhub' when absent
}

export interface UpdateExecutorRequest {
  // …existing fields…
  protocol?: ExecutorProtocol;
}
```

### Step 2: Add the protocol form control to the executor modal
**File:** `client/src/app/features/admin/executors/executor-form.modal.ts` · Modify

Inside the form group:

```ts
protocol: new FormControl<ExecutorProtocol>('orchestrator', {
  nonNullable: true,
  validators: [Validators.required],
}),
```

Default to `'orchestrator'` — the real production path. Edit mode pre-fills from the existing value.

On submit, include `protocol` in the emitted request body.

### Step 3: Render the dropdown
**File:** `client/src/app/features/admin/executors/executor-form.modal.html` · Modify

Add after the existing fields (around the Base URL row):

```html
<app-form-field label="Protocol" [required]="true">
  <select formControlName="protocol"
          class="block w-full border border-slate-300 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 rounded-lg px-3 h-10 outline-none">
    <option value="orchestrator">orchestrator — carestechs-agent-orchestrator (/api/v1/runs)</option>
    <option value="devhub">devhub — DevHub-native protocol (tests / FakeExecutor)</option>
  </select>
  <p class="text-xs text-slate-500 mt-1">
    Pick <strong>orchestrator</strong> for production registrations against the carestechs-agent-orchestrator.
    Pick <strong>devhub</strong> only for test executors or legacy registrations.
  </p>
</app-form-field>
```

### Step 4: List + detail surfaces
**File:** `client/src/app/features/admin/executors/executors.page.html` · Modify

Add a Protocol column to the executors table (compact, monospace).

### Step 5: WorkItem detail — executor run id label
**File:** `client/src/app/core/api/work-items.types.ts` · Modify

```ts
export interface WorkItemSummaryDto {
  // …existing fields…
  executorRunId?: string | null;
}
```

(Already inherited by `WorkItemDto extends WorkItemSummaryDto`.)

**File:** `client/src/app/features/projects/work-items/work-item-detail.page.html` · Modify

Find the metadata strip near the top (after the marker label). Append:

```html
@if (workItem()?.executorRunId; as rid) {
  <span class="text-xs text-slate-500">
    run: <span class="font-mono">{{ rid }}</span>
  </span>
}
```

Tiny — purely for ops debugging. Operators can copy the run id and grep the orchestrator's logs.

### Step 6: Specs
**File:** `client/src/app/features/admin/executors/executor-form.modal.spec.ts` · Modify

Three specs:
- Create form default is `'orchestrator'`; submit emits the value.
- Toggle to `'devhub'` and submit emits the new value.
- Edit mode pre-fills from the input DTO.

**File:** `client/src/app/features/projects/work-items/work-item-detail.page.spec.ts` · Modify

One spec: `executorRunId` from the fixture renders next to the marker; absent value renders nothing.

### Step 7: Build + test
**Bash:**

```bash
cd client && npx ng build --configuration development
npx ng test --watch=false --browsers=ChromeHeadless
```

165 + new specs all green.

### Step 8: Update `docs/ui-specification.md`
**File:** `docs/ui-specification.md` · Modify

Executor admin section: note the Protocol field + copy. WorkItem detail: note the run id label. Changelog entry.

## Files Affected
| File | Action |
|---|---|
| `client/src/app/core/api/executor-registry.types.ts` | Modify |
| `client/src/app/core/api/work-items.types.ts` | Modify |
| `client/src/app/features/admin/executors/executor-form.modal.{ts,html,spec.ts}` | Modify |
| `client/src/app/features/admin/executors/executors.page.html` | Modify |
| `client/src/app/features/projects/work-items/work-item-detail.page.{html,spec.ts}` | Modify |
| `docs/ui-specification.md` | Modify |

## Edge Cases & Risks
- **Defaulting to `orchestrator` for new executors** is a behavioral change. Document in the modal's helper text. Existing executors keep `devhub` (server-side default, no UI involvement).
- **Switching an existing executor's protocol** could orphan in-flight work items (their `ExecutorRunId` was populated by one client; subsequent calls hit a different client). v1 acceptable — admin UI shows a warning when changing protocol on an executor with active work items: "Existing work items will continue to use the old protocol; new ones use the new one." If that's too loose, FEAT-011 can split into "Protocol can't change once set."

## Acceptance Verification
- [ ] Create form has a Protocol dropdown defaulting to `'orchestrator'`.
- [ ] Update form pre-fills from the executor's stored value.
- [ ] WorkItem detail header shows the run id when present.
- [ ] All existing executor + work-item-detail specs still pass.
