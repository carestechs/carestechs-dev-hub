import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, input, signal } from '@angular/core';
import { MarkdownRenderer } from './markdown-renderer';
import type { PanelSubmit } from './panel-submit';

interface CurrentTask {
  id?: string;
  title?: string;
}

interface ReviewEntry {
  taskId?: string;
  attempt?: number;
  verdict?: string;
  feedback?: string;
}

interface ImplementationRef {
  prUrl?: string | null;
  commitSha?: string | null;
  summary?: string | null;
}

@Component({
  selector: 'final-review-panel',
  standalone: true,
  imports: [MarkdownRenderer],
  templateUrl: './final-review-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FinalReviewPanel {
  readonly artefact = input<unknown>(null);
  readonly currentTaskId = input<string | null>(null);
  readonly canAct = input<boolean>(false);
  readonly working = input<boolean>(false);

  @Output() readonly submitted = new EventEmitter<PanelSubmit>();

  protected readonly rejecting = signal(false);
  protected readonly feedback = signal('');

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

  protected readonly implementationRef = computed<ImplementationRef | null>(() => {
    const raw = this.artefact();
    if (!raw || typeof raw !== 'object') return null;
    const obj = raw as Record<string, unknown>;
    const ref = obj['implementationRef'];
    if (!ref || typeof ref !== 'object') return null;
    const r = ref as Record<string, unknown>;
    return {
      prUrl: typeof r['prUrl'] === 'string' ? r['prUrl'] : null,
      commitSha: typeof r['commitSha'] === 'string' ? r['commitSha'] : null,
      summary: typeof r['summary'] === 'string' ? r['summary'] : null,
    };
  });

  protected readonly reviewHistory = computed<ReviewEntry[]>(() => {
    const raw = this.artefact();
    if (!raw || typeof raw !== 'object') return [];
    const obj = raw as Record<string, unknown>;
    const hist = obj['reviewHistory'];
    if (!Array.isArray(hist)) return [];
    return hist
      .filter((r): r is Record<string, unknown> => r != null && typeof r === 'object')
      .map(r => ({
        taskId: typeof r['task_id'] === 'string' ? r['task_id'] : undefined,
        attempt: typeof r['attempt'] === 'number' ? r['attempt'] : undefined,
        verdict: typeof r['verdict'] === 'string' ? r['verdict'] : undefined,
        feedback: typeof r['feedback'] === 'string' ? r['feedback'] : undefined,
      }));
  });

  protected readonly canSubmit = computed(() =>
    this.canAct() && !this.working());

  protected readonly canReject = computed(() =>
    this.canAct() && !this.working() && this.feedback().trim().length > 0);

  protected onApprove(): void {
    if (!this.canSubmit()) return;
    this.submitted.emit({
      outcome: 'approve',
      payload: { verdict: 'pass' },
      taskId: this.currentTaskId(),
    });
  }

  protected onReject(): void {
    if (!this.canReject()) return;
    this.submitted.emit({
      outcome: 'approve',
      payload: { verdict: 'fail', feedback: this.feedback().trim() },
      taskId: this.currentTaskId(),
    });
  }

  protected toggleReject(): void {
    this.rejecting.update(v => !v);
    this.feedback.set('');
  }
}
