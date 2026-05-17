import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { ProjectMembershipDto } from '../../../../core/api/workspace.types';

export interface AssignmentConfirmSubmit {
  outcome: string;
  payload: { assignee: string };
  taskId: string | null;
}

/**
 * Lifecycle review panel variant for the `assignment-confirmed` checkpoint (FEAT-009 / IMP-005).
 * Rendered instead of {@link CheckpointActionBar} when the active contract has `perTask=true`
 * AND `checkpointKey === 'assignment-confirmed'`.
 *
 * Two modes:
 * - **Project member picker** — `displayName` of the selected membership is the assignee value.
 * - **Other (free text)** — operator types any string; sent verbatim to the executor.
 *
 * Submit emits the assignee + the current task id (passed in via input); the parent calls
 * `WorkItemsService.signal(...)` and refetches.
 */
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
  readonly canAct = input<boolean>(false);
  readonly working = input<boolean>(false);

  @Output() readonly submitted = new EventEmitter<AssignmentConfirmSubmit>();

  protected readonly mode = signal<'picker' | 'freetext'>('picker');
  protected readonly selectedMemberId = signal<string>('');
  protected readonly freeTextValue = signal<string>('');

  protected readonly currentValue = computed<string>(() => {
    if (this.mode() === 'freetext') return this.freeTextValue().trim();
    const id = this.selectedMemberId();
    const m = this.memberships().find(mm => mm.member.id === id);
    return m?.member.displayName ?? '';
  });

  protected readonly canSubmit = computed(() =>
    this.canAct() && !this.working() && this.currentValue().length > 0);

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
