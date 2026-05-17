# Implementation Plan: T-069 — SPA types for per-task fields

## Task Reference
- **Task ID:** T-069 · **Type:** Frontend · **Workflow:** standard · **Complexity:** S
- **Rationale:** Wire-level parity. T-070 / T-071 / T-072 depend on these types.

## Overview
Three small TS additions. No new service methods — the existing `WorkItemsService.signal(...)` and `NotificationsService.list*(...)` accept / return the wider shape automatically.

## Implementation Steps

### Step 1: Extend `PendingActionDto`
**File:** `client/src/app/core/api/notifications.types.ts` · Modify

```ts
export interface PendingActionDto {
  // …existing fields…
  taskId?: string | null;
}
```

### Step 2: Extend `SignalRequest`
**File:** `client/src/app/core/api/work-items.types.ts` · Modify

```ts
export interface SignalRequest {
  outcome: string;
  payload?: unknown;
  taskId?: string;
}
```

### Step 3: Extend `CheckpointContractView` (executor-registry side)
**File:** `client/src/app/core/api/executor-registry.types.ts` · Modify

```ts
export interface CheckpointContractView {
  // …existing fields…
  perTask: boolean;
}
```

The backend always sends the field; default-to-false is the backend's responsibility.

### Step 4: Smoke the existing service specs
**File:** `client/src/app/core/api/work-items.service.spec.ts` and any other service specs that use the affected DTOs · Verify

Existing specs use partial fixtures (e.g., `{ id: 'w1', title: 'X' }`) — they don't break on type widening. Run the suite to confirm.

### Step 5: Build + test
**Bash:**

```bash
cd client && npx ng build --configuration development
npx ng test --watch=false --browsers=ChromeHeadless
```

156/156 still green.

## Files Affected
| File | Action |
|------|--------|
| `client/src/app/core/api/notifications.types.ts` | Modify |
| `client/src/app/core/api/work-items.types.ts` | Modify |
| `client/src/app/core/api/executor-registry.types.ts` | Modify |

## Edge Cases & Risks
- **`taskId?: string | null` vs `string | undefined`** — System.Text.Json emits `"taskId": null` rather than omitting the property. TypeScript's `?:` covers both undefined and null when the field is absent. Use `string | null` to be explicit about the JSON shape.

## Acceptance Verification
- [ ] Three types extended.
- [ ] `ng build` clean.
- [ ] `ng test` still green.
