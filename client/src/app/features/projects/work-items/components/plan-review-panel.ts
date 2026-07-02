import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, input, signal } from '@angular/core';
import { MarkdownRenderer } from './markdown-renderer';
import type { PanelSubmit } from './panel-submit';

interface CurrentTask {
  id?: string;
  title?: string;
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

  protected readonly planMarkdown = computed(() => {
    const raw = this.artefact();
    if (!raw || typeof raw !== 'object') return '';
    const obj = raw as Record<string, unknown>;
    return typeof obj['planMarkdown'] === 'string' ? obj['planMarkdown'] : '';
  });

  protected readonly currentTask = computed<CurrentTask>(() => {
    const raw = this.artefact();
    if (!raw || typeof raw !== 'object') return {};
    const obj = raw as Record<string, unknown>;
    const ct = obj['currentTask'];
    if (!ct || typeof ct !== 'object') return {};
    const t = ct as Record<string, unknown>;
    return {
      id: typeof t['id'] === 'string' ? t['id'] : undefined,
      title: typeof t['title'] === 'string' ? t['title'] : undefined,
    };
  });

  protected readonly taskLabel = computed(() =>
    this.currentTask().id ?? this.currentTaskId() ?? null);

  protected readonly canSubmit = computed(() =>
    this.canAct() && !this.working());

  protected readonly canReject = computed(() =>
    this.canAct() && !this.working() && this.feedback().trim().length > 0);

  protected onConfirm(): void {
    if (!this.canSubmit()) return;
    this.submitted.emit({
      outcome: 'approve',
      payload: { verdict: 'approve' },
      taskId: this.currentTaskId(),
    });
  }

  protected onReject(): void {
    if (!this.canReject()) return;
    this.submitted.emit({
      outcome: 'approve',
      payload: { verdict: 'reject', feedback: this.feedback().trim() },
      taskId: this.currentTaskId(),
    });
  }

  protected toggleReject(): void {
    this.rejecting.update(v => !v);
    this.feedback.set('');
  }
}
