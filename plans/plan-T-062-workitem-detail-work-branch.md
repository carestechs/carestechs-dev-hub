# Implementation Plan: T-062 — WorkItem detail "Effective branch" + inline edit

## Task Reference
- **Task ID:** T-062 · **Type:** Frontend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-9. Closes the read+write loop on the new optional `workBranch` override.

## Overview
One new row on the WorkItem detail page showing the effective branch (`workBranch ?? project.defaultBranch ?? "(not set)"`). Operator-only pencil affordance opens an inline edit; empty submit clears the override.

## Implementation Steps

### Step 1: Read the work item + its project together
**File:** `client/src/app/features/projects/work-items/work-item-detail.page.ts` · Modify

The page should already have access to both signals. If not, augment with a `project` signal sourced from `WorkspaceService.getProjectBySlug` keyed by the route's `:projectSlug`.

Add `branchEditOpen`, `branchSaving`, `branchError` signals. Compute the effective branch in the template (no signal needed):

```html
<span>{{ workItem()?.workBranch ?? project()?.defaultBranch ?? '(not set)' }}</span>
```

### Step 2: Add the Branch row to the detail layout
**File:** `client/src/app/features/projects/work-items/work-item-detail.page.html` · Modify

Inside the metadata strip:

```html
<div class="flex items-center gap-2">
  <div>
    <p class="text-xs text-slate-500">Branch</p>
    <p class="text-sm font-mono">
      @if (workItem()?.workBranch) {
        <span>{{ workItem()!.workBranch }}</span>
        <span class="text-xs text-slate-400 ml-2">(override)</span>
      } @else if (project()?.defaultBranch) {
        <span>{{ project()!.defaultBranch }}</span>
        <span class="text-xs text-slate-400 ml-2">(project default)</span>
      } @else {
        <span class="italic text-slate-400">(not set)</span>
      }
    </p>
  </div>
  @if (isOperator()) {
    <button type="button" (click)="openBranchEdit()"
            class="text-sky-600 hover:text-sky-800 text-xs">Edit</button>
  }
</div>
```

### Step 3: Render the inline edit form
**File:** `client/src/app/features/projects/work-items/work-item-detail.page.html` · Modify

Below the metadata strip (or as a small modal — pick whichever matches existing detail-page conventions):

```html
@if (branchEditOpen()) {
  <form (ngSubmit)="submitBranch()" class="flex items-center gap-2 mt-2">
    <input #branchInput type="text" [value]="workItem()?.workBranch ?? ''"
           placeholder="leave empty to use project default"
           class="border border-slate-300 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 rounded-lg px-3 h-9 text-sm font-mono" />
    <app-button variant="primary" type="submit" [loading]="branchSaving()">Save</app-button>
    <app-button variant="secondary" type="button" (clicked)="closeBranchEdit()">Cancel</app-button>
    @if (branchError()) {
      <span class="text-xs text-red-600">{{ branchError() }}</span>
    }
  </form>
}
```

### Step 4: Wire the submit
**File:** `client/src/app/features/projects/work-items/work-item-detail.page.ts` · Modify

```ts
async submitBranch(): Promise<void> {
  const value = this.branchInputRef().nativeElement.value.trim();
  // Client-side validation mirrors the backend rules.
  if (value && !this.branchIsValid(value)) {
    this.branchError.set('Invalid branch: no whitespace, no leading "/", no "..".');
    return;
  }
  this.branchSaving.set(true);
  this.branchError.set(null);
  try {
    const updated = await this.workItems.updateWorkItem(
      this.projectId(), this.workItemId(),
      { workBranch: value === '' ? null : value });
    this.workItem.set(updated);
    this.branchEditOpen.set(false);
  } catch (e: unknown) {
    this.branchError.set(this.toAppError(e).title);
  } finally {
    this.branchSaving.set(false);
  }
}

private branchIsValid(s: string): boolean {
  if (s[0] === '/') return false;
  if (s.includes('..')) return false;
  for (const ch of s) {
    if (ch.charCodeAt(0) < 0x20 || ch.charCodeAt(0) === 0x7F) return false;
    if (/\s/.test(ch)) return false;
  }
  return true;
}
```

The empty-string submit sends `null` so the backend clears the override (T-058 step 4 contract).

### Step 5: Update specs
**File:** `client/src/app/features/projects/work-items/work-item-detail.page.spec.ts` · Modify

Add cases: "Branch row shows effective value from workBranch when set", "Branch row falls back to project default when workBranch null", "Edit button hidden for non-operators", "Submit empty clears the override (PATCH body workBranch: null)", "Submit invalid branch surfaces inline error and does not call API".

### Step 6: Update `docs/ui-specification.md`
**File:** `docs/ui-specification.md` · Modify

WorkItem detail screen section: add a "Branch" row to the metadata strip with the effective-branch logic. Add changelog row:

```
| 2026-05-17 | FEAT-008 | WorkItem detail: added "Branch" row with operator-only inline edit. |
```

### Step 7: Build + smoke
**Bash:**

```bash
cd client && npx ng build --configuration development
npx ng test --watch=false --browsers=ChromeHeadless
```

Manual smoke: open a work item → see project default → click Edit → enter `feat/x` → save → label now shows `feat/x (override)` → Edit again → clear → save → label shows project default again.

## Files Affected
| File | Action |
|------|--------|
| `client/src/app/features/projects/work-items/work-item-detail.page.ts` | Modify |
| `client/src/app/features/projects/work-items/work-item-detail.page.html` | Modify |
| `client/src/app/features/projects/work-items/work-item-detail.page.spec.ts` | Modify |
| `docs/ui-specification.md` | Modify |

## Edge Cases & Risks
- **In-flight runs ignore the change.** Editing `workBranch` after start does not affect the in-flight executor run — the value was forwarded at start time and the executor doesn't refetch. UI surfaces this via the `(override)` label (and we could add a future tooltip if it confuses operators). Documented in the brief's §9 already.
- **The project signal availability.** Work item detail pages today may or may not load the parent project. If not, the page must do an extra fetch on init. Acceptable; the page already does several parallel loads.
- **Client-side branch validation drift from backend.** Keep the two in sync: T-056 is the source of truth. If a future tightening lands, update both within the same PR.

## Acceptance Verification
- [ ] Branch row visible on the detail page with the right label (`(override)` / `(project default)` / `(not set)`).
- [ ] Operator can edit; non-operator cannot see the button.
- [ ] Empty submit clears the override; the row falls back to project default.
- [ ] Invalid input surfaces inline error and does not hit the API.
- [ ] `ui-specification.md` updated + changelog row.
