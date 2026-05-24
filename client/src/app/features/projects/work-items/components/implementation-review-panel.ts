import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, input, signal } from '@angular/core';
import type { PanelSubmit } from './panel-submit';

@Component({
  selector: 'implementation-review-panel',
  standalone: true,
  templateUrl: './implementation-review-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ImplementationReviewPanel {
  readonly currentTaskId = input<string | null>(null);
  readonly workBranch = input<string | null>(null);
  readonly canAct = input<boolean>(false);
  readonly working = input<boolean>(false);

  @Output() readonly submitted = new EventEmitter<PanelSubmit>();

  protected readonly requesting = signal(false);
  protected readonly summary = signal('');
  protected readonly commitSha = signal('');
  protected readonly prUrl = signal('');
  protected readonly feedback = signal('');

  protected readonly canComplete = computed(() =>
    this.canAct() && !this.working());

  protected readonly canRequestChanges = computed(() =>
    this.canAct() && !this.working() && this.feedback().trim().length > 0);

  protected onComplete(): void {
    if (!this.canComplete()) return;
    const payload: Record<string, unknown> = {};
    const s = this.summary().trim();
    const c = this.commitSha().trim();
    const p = this.prUrl().trim();
    if (s) payload['summary'] = s;
    if (c) payload['commitSha'] = c;
    if (p) payload['prUrl'] = p;
    this.submitted.emit({
      outcome: 'complete',
      payload,
      taskId: this.currentTaskId(),
    });
  }

  protected onRequestChanges(): void {
    if (!this.canRequestChanges()) return;
    this.submitted.emit({
      outcome: 'changes-requested',
      payload: { feedback: this.feedback().trim() },
      taskId: this.currentTaskId(),
    });
  }

  protected toggleRequest(): void {
    this.requesting.update(v => !v);
    this.feedback.set('');
  }
}
