import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, input, signal } from '@angular/core';
import { MarkdownRenderer } from './markdown-renderer';
import type { PanelSubmit } from './panel-submit';

interface PlanArtefact {
  plan_markdown?: string;
  task_id?: string;
}

@Component({
  selector: 'plan-review-panel',
  standalone: true,
  imports: [MarkdownRenderer],
  templateUrl: './plan-review-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlanReviewPanel {
  readonly artefact = input<unknown>(null);
  readonly currentTaskId = input<string | null>(null);
  readonly canAct = input<boolean>(false);
  readonly working = input<boolean>(false);

  @Output() readonly submitted = new EventEmitter<PanelSubmit>();

  protected readonly rejecting = signal(false);
  protected readonly feedback = signal('');

  protected readonly parsed = computed<PlanArtefact>(() => {
    const raw = this.artefact();
    if (raw && typeof raw === 'object') {
      const obj = raw as Record<string, unknown>;
      return {
        plan_markdown: typeof obj['plan_markdown'] === 'string' ? obj['plan_markdown'] : undefined,
        task_id: typeof obj['task_id'] === 'string' ? obj['task_id'] : undefined,
      };
    }
    return {};
  });

  protected readonly taskLabel = computed(() =>
    this.parsed().task_id ?? this.currentTaskId() ?? null);

  protected readonly canSubmit = computed(() =>
    this.canAct() && !this.working());

  protected readonly canReject = computed(() =>
    this.canAct() && !this.working() && this.feedback().trim().length > 0);

  protected onConfirm(): void {
    if (!this.canSubmit()) return;
    this.submitted.emit({
      outcome: 'confirmed',
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
