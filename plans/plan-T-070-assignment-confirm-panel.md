# Implementation Plan: T-070 — `AssignmentConfirmPanel` (member-picker + free-text fallback)

## Task Reference
- **Task ID:** T-070 · **Type:** Frontend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-4. The operator-facing input form for `assignment-confirmed` signals.

## Overview
New `AssignmentConfirmPanel` component, swapped in by the review page when the active contract is `perTask=true` + `checkpointKey="assignment-confirmed"`. Renders a member-picker (scoped to project memberships) plus a "Other (free text)" toggle. Submits `{ outcome: "confirmed", payload: { assignee }, taskId }`.

## Implementation Steps

### Step 1: Create the component shell
**File:** `client/src/app/features/projects/work-items/components/assignment-confirm-panel.ts` · Create

```ts
import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { ProjectMembershipDto } from '../../../../core/api/workspace.types';

export interface AssignmentConfirmSubmit {
  outcome: string;
  payload: { assignee: string };
  taskId: string | null;
}

@Component({
  selector: 'assignment-confirm-panel',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './assignment-confirm-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AssignmentConfirmPanel {
  readonly memberships = input<ProjectMembershipDto[]>([]);
  readonly currentTaskId = input<string | null>(null);
  readonly submitting = input<boolean>(false);

  @Output() readonly submitted = new EventEmitter<AssignmentConfirmSubmit>();

  protected readonly mode = signal<'picker' | 'freetext'>('picker');
  protected readonly selectedMemberId = signal<string>('');
  protected readonly freeTextValue = signal<string>('');

  protected readonly currentValue = computed<string>(() => {
    if (this.mode() === 'freetext') return this.freeTextValue().trim();
    const id = this.selectedMemberId();
    const m = this.memberships().find(m => m.member.id === id);
    return m?.member.displayName ?? '';
  });

  protected readonly canSubmit = computed(() =>
    !this.submitting() && this.currentValue().length > 0);

  protected onSubmit(): void {
    const assignee = this.currentValue();
    if (!assignee) return;
    this.submitted.emit({
      outcome: 'confirmed',
      payload: { assignee },
      taskId: this.currentTaskId(),
    });
  }
}
```

### Step 2: Component template
**File:** `client/src/app/features/projects/work-items/components/assignment-confirm-panel.html` · Create

```html
<div class="bg-white rounded-xl shadow-sm p-6">
  <h2 class="text-lg font-semibold mb-1">Confirm task assignment</h2>
  <p class="text-sm text-slate-500 mb-4">
    @if (currentTaskId(); as t) { Pick the assignee for <span class="font-mono">{{ t }}</span>. }
    @else { Pick the assignee for this task. }
  </p>

  <div class="flex items-center gap-4 mb-3 text-sm">
    <label class="flex items-center gap-2">
      <input type="radio" name="mode" [checked]="mode() === 'picker'" (change)="mode.set('picker')">
      <span>Project member</span>
    </label>
    <label class="flex items-center gap-2">
      <input type="radio" name="mode" [checked]="mode() === 'freetext'" (change)="mode.set('freetext')">
      <span>Other (free text)</span>
    </label>
  </div>

  @if (mode() === 'picker') {
    <select [ngModel]="selectedMemberId()" (ngModelChange)="selectedMemberId.set($event)"
            class="block w-full border border-slate-300 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 rounded-lg px-3 h-10 outline-none">
      <option value="" disabled>— select a member —</option>
      @for (m of memberships(); track m.id) {
        <option [value]="m.member.id">{{ m.member.displayName }}</option>
      }
    </select>
  } @else {
    <input type="text" [ngModel]="freeTextValue()" (ngModelChange)="freeTextValue.set($event)"
           placeholder="Assignee name or handle"
           class="block w-full border border-slate-300 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 rounded-lg px-3 h-10 outline-none" />
  }

  <div class="flex justify-end mt-4">
    <button type="button"
            class="bg-sky-500 disabled:bg-slate-300 text-white px-4 h-10 rounded-lg hover:bg-sky-600 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-300"
            [disabled]="!canSubmit()"
            (click)="onSubmit()">
      @if (submitting()) { Submitting… } @else { Confirm assignment }
    </button>
  </div>
</div>
```

### Step 3: Wire into the review page
**File:** `client/src/app/features/projects/work-items/review.page.ts` · Modify

Add a signal for project memberships (one-shot fetch on init). Inject `WorkspaceService.listMemberships(projectId)`. Swap logic in the template lives in step 4.

```ts
protected readonly memberships = signal<ProjectMembershipDto[]>([]);

// Inside the existing load() after the project is fetched:
this.memberships.set(await this.ws.listMemberships(project.id));
```

Wrap in `try/catch` and swallow with `memberships.set([])` on failure (the panel falls back to free-text-only when the list is empty).

Add a submit handler that calls `WorkItemsService.signal(...)`:

```ts
protected async onAssignmentSubmit(req: AssignmentConfirmSubmit): Promise<void> {
  const p = this.project(); const wi = this.workItem();
  if (!p || !wi) return;
  this.submitting.set('assignment-confirmed');
  this.submitError.set(null);
  try {
    await this.workItems.signal(p.id, wi.id, 'assignment-confirmed',
      { outcome: req.outcome, payload: req.payload, taskId: req.taskId ?? undefined },
      crypto.randomUUID());
    // Existing handler refreshes the work item + signal history.
    await this.refresh();
  } catch (e: unknown) {
    this.submitError.set(this.toAppError(e));
  } finally {
    this.submitting.set(null);
  }
}
```

(Adjust to match the existing service signature for `signal` — the idempotency key argument may already be inferred.)

### Step 4: Swap panel in the template
**File:** `client/src/app/features/projects/work-items/review.page.html` · Modify

Where `CheckpointActionBar` is currently rendered:

```html
@if (contract(); as c) {
  @if (c.perTask && c.checkpointKey === 'assignment-confirmed') {
    <assignment-confirm-panel
      [memberships]="memberships()"
      [currentTaskId]="workItem()?.currentTaskId ?? null"
      [submitting]="submitting() === 'assignment-confirmed'"
      (submitted)="onAssignmentSubmit($event)" />
  } @else {
    <!-- existing CheckpointActionBar -->
  }
}
```

### Step 5: Specs
**File:** `client/src/app/features/projects/work-items/components/assignment-confirm-panel.spec.ts` · Create

Three specs:

1. **Picker mode submit emits the right payload.** Set `memberships` to a list with one member ("Alice"), select her, click Confirm → assertion that `submitted` emitted `{ outcome: 'confirmed', payload: { assignee: 'Alice' }, taskId: <input> }`.
2. **Free-text mode submit emits the typed value.** Switch to free-text, type "external-vendor", submit → same shape with the typed string.
3. **Empty value disables the button.** Picker mode with no selection → button is disabled; free-text mode with empty input → also disabled.

### Step 6: Build + test
**Bash:**

```bash
cd client && npx ng build --configuration development
npx ng test --watch=false --browsers=ChromeHeadless
```

Existing 156 specs still green, +3 from the new panel.

### Step 7: Update `docs/ui-specification.md`
**File:** `docs/ui-specification.md` · Modify

WorkItemDetailPage section: note the review-page swap behavior. Add a "When the active checkpoint is `assignment-confirmed`" subsection. Changelog entry:

```
| 2026-05-17 (FEAT-009 / T-070) | New AssignmentConfirmPanel on the lifecycle review page. Renders when contract has perTask=true and checkpointKey="assignment-confirmed". Member-picker scoped to project memberships + free-text escape hatch. Submits to the existing /signal endpoint with payload.assignee and taskId. |
```

## Files Affected
| File | Action |
|------|--------|
| `client/src/app/features/projects/work-items/components/assignment-confirm-panel.ts` + `.html` + `.spec.ts` | Create |
| `client/src/app/features/projects/work-items/review.page.ts` + `.html` | Modify |
| `docs/ui-specification.md` | Modify |

## Edge Cases & Risks
- **Memberships fetch fails.** Panel falls back to free-text-only (empty list shown). Operators can still complete the flow.
- **Operator is in the picker mode and types into the free-text field that's hidden.** Reactive form state doesn't see it. Mode swap rebinds — the displayed control is the only one that matters.
- **Long member list.** Native select handles it; no virtualization needed in v1.

## Acceptance Verification
- [ ] Panel renders only when contract is `perTask=true` + `assignment-confirmed`.
- [ ] Picker submit + free-text submit + empty-disabled all covered by specs.
- [ ] Submit emits the right shape; review-page handler forwards to `WorkItemsService.signal`.
- [ ] `docs/ui-specification.md` updated.
