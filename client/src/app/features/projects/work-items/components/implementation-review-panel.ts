import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, effect, input, signal } from '@angular/core';
import { MarkdownRenderer } from './markdown-renderer';
import type { PanelSubmit } from './panel-submit';

interface CurrentTask {
  id?: string;
  title?: string;
}

@Component({
  selector: 'implementation-review-panel',
  standalone: true,
  imports: [MarkdownRenderer],
  templateUrl: './implementation-review-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ImplementationReviewPanel {
  readonly artefact = input<unknown>(null);
  readonly currentTaskId = input<string | null>(null);
  readonly workBranch = input<string | null>(null);
  readonly prUrl = input<string | null>(null);
  readonly canAct = input<boolean>(false);
  readonly working = input<boolean>(false);

  @Output() readonly submitted = new EventEmitter<PanelSubmit>();

  protected readonly requesting = signal(false);
  protected readonly summary = signal('');
  protected readonly commitSha = signal('');
  protected readonly prUrlValue = signal('');
  protected readonly feedback = signal('');

  constructor() {
    effect(() => {
      const existing = this.prUrl();
      if (existing) this.prUrlValue.set(existing);
    });
  }

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

  protected readonly planMarkdown = computed(() => {
    const raw = this.artefact();
    if (!raw || typeof raw !== 'object') return '';
    const obj = raw as Record<string, unknown>;
    return typeof obj['planMarkdown'] === 'string' ? obj['planMarkdown'] : '';
  });

  protected readonly canComplete = computed(() =>
    this.canAct() && !this.working() && this.prUrlValue().trim().length > 0);

  protected readonly canRequestChanges = computed(() =>
    this.canAct() && !this.working() && this.feedback().trim().length > 0);

  protected onComplete(): void {
    if (!this.canComplete()) return;
    const payload: Record<string, unknown> = {};
    const s = this.summary().trim();
    const c = this.commitSha().trim();
    const p = this.prUrlValue().trim();
    if (s) payload['summary'] = s;
    if (c) payload['commitSha'] = c;
    if (p) payload['prUrl'] = p;
    this.submitted.emit({
      outcome: 'approve',
      payload,
      taskId: this.currentTaskId(),
    });
  }

  protected onRequestChanges(): void {
    if (!this.canRequestChanges()) return;
    this.submitted.emit({
      outcome: 'approve',
      payload: { verdict: 'reject', feedback: this.feedback().trim() },
      taskId: this.currentTaskId(),
    });
  }

  protected toggleRequest(): void {
    this.requesting.update(v => !v);
    this.feedback.set('');
  }
}
