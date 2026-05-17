# Implementation Plan: T-060 — SPA types + service methods

## Task Reference
- **Task ID:** T-060 · **Type:** Frontend · **Workflow:** standard · **Complexity:** S
- **Rationale:** Wire-level parity with the backend so T-061 and T-062 have the types and methods they need.

## Overview
Three small TS edits + two new service methods. Mirrors what T-057 and T-058 shipped on the backend.

## Implementation Steps

### Step 1: Extend workspace types
**File:** `client/src/app/core/api/workspace.types.ts` · Modify

Add `repo?: string` and `defaultBranch?: string` to `ProjectDto`, `CreateProjectRequest`, and `UpdateProjectRequest`. Place them next to `description`.

### Step 2: Confirm `WorkspaceService.updateProject` exists
**File:** `client/src/app/core/api/workspace.service.ts` · Modify

If a `updateProject(id, body)` method does not already exist, add it following the `updateTeam` pattern:

```ts
updateProject(id: string, body: UpdateProjectRequest): Promise<ProjectDto> {
  return this.patchUnwrap(`/api/projects/${id}`, body);
}
```

(`patchUnwrap` is the existing helper — confirm and follow the same pattern as `updateTeam`.)

### Step 3: Extend work-item types
**File:** `client/src/app/core/api/work-items.types.ts` · Modify

```ts
export interface WorkItemDto {
  // …existing fields…
  workBranch: string | null;
}

export interface StartWorkItemRequest {
  // …existing fields…
  workBranch?: string;
}

export interface UpdateWorkItemRequest {
  workBranch?: string | null;  // null + empty string both clear; undefined = leave unchanged
}
```

The `null` vs `undefined` distinction matches the backend's empty-string-means-clear convention (T-058 step 4).

### Step 4: Add `WorkItemsService.updateWorkItem`
**File:** `client/src/app/core/api/work-items.service.ts` · Modify

```ts
updateWorkItem(projectId: string, workItemId: string, body: UpdateWorkItemRequest): Promise<WorkItemDto> {
  return this.patchUnwrap(`/api/projects/${projectId}/work-items/${workItemId}`, body);
}
```

### Step 5: Add minimal request-asserting specs
**File:** `client/src/app/core/api/workspace.service.spec.ts` · Modify

Add a single spec asserting that `updateProject('p1', { repo: 'a/b' })` issues a `PATCH /api/projects/p1` with the right body and unwraps the envelope. Follow the existing `updateTeam` spec for shape.

**File:** `client/src/app/core/api/work-items.service.spec.ts` · Modify

Mirror for `updateWorkItem`.

### Step 6: Build + test
**Bash:**

```bash
cd client && npx ng build --configuration development
npx ng test --watch=false --browsers=ChromeHeadless
```

All existing specs pass; the two new ones run cleanly.

## Files Affected
| File | Action |
|------|--------|
| `client/src/app/core/api/workspace.types.ts` | Modify |
| `client/src/app/core/api/workspace.service.ts` | Modify |
| `client/src/app/core/api/work-items.types.ts` | Modify |
| `client/src/app/core/api/work-items.service.ts` | Modify |
| `client/src/app/core/api/workspace.service.spec.ts` | Modify |
| `client/src/app/core/api/work-items.service.spec.ts` | Modify |

## Edge Cases & Risks
- **The `WorkItemDto.workBranch` field landing as `null` vs `undefined`.** System.Text.Json emits absent properties; for a nullable serialized field, .NET will emit `"workBranch": null`. TS treats this as `null`. Don't widen the type to `string | null | undefined`.
- **Existing positional consumers of `ProjectDto`.** TypeScript interfaces aren't positional, so adding optional fields is safe. No `.spec.ts` mock object should need updating — they all use partial fixtures.

## Acceptance Verification
- [ ] `ng build` clean.
- [ ] `ng test` green.
- [ ] New service methods present and spec'd.
