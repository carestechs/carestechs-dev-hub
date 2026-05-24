import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, input, signal } from '@angular/core';
import type { PanelSubmit } from './panel-submit';

@Component({
  selector: 'final-review-panel',
  standalone: true,
  templateUrl: './final-review-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FinalReviewPanel {
  readonly currentTaskId = input<string | null>(null);
  readonly canAct = input<boolean>(false);
  readonly working = input<boolean>(false);

  @Output() readonly submitted = new EventEmitter<PanelSubmit>();

  protected readonly rejecting = signal(false);
  protected readonly feedback = signal('');

  protected readonly canSubmit = computed(() =>
    this.canAct() && !this.working());

  protected readonly canReject = computed(() =>
    this.canAct() && !this.working() && this.feedback().trim().length > 0);

  protected onApprove(): void {
    if (!this.canSubmit()) return;
    this.submitted.emit({
      outcome: 'approved',
      payload: {},
      taskId: this.currentTaskId(),
    });
  }

  protected onReject(): void {
    if (!this.canReject()) return;
    this.submitted.emit({
      outcome: 'rejected',
      payload: { feedback: this.feedback().trim() },
      taskId: this.currentTaskId(),
    });
  }

  protected toggleReject(): void {
    this.rejecting.update(v => !v);
    this.feedback.set('');
  }
}
