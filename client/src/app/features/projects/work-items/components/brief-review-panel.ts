import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, effect, input, signal } from '@angular/core';
import { MarkdownRenderer } from './markdown-renderer';
import type { PanelSubmit } from './panel-submit';

interface WorkItemSummary {
  id?: string;
  type?: string;
  title?: string;
  path?: string;
}

@Component({
  selector: 'brief-review-panel',
  standalone: true,
  imports: [MarkdownRenderer],
  templateUrl: './brief-review-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BriefReviewPanel {
  readonly artefact = input<unknown>(null);
  readonly canAct = input<boolean>(false);
  readonly working = input<boolean>(false);

  @Output() readonly submitted = new EventEmitter<PanelSubmit>();

  protected readonly rejecting = signal(false);
  protected readonly title = signal('');
  protected readonly type = signal<'FEAT' | 'BUG' | 'IMP'>('FEAT');
  protected readonly feedback = signal('');

  protected readonly briefContent = computed(() => {
    const raw = this.artefact();
    if (!raw || typeof raw !== 'object') return '';
    const obj = raw as Record<string, unknown>;
    const wi = obj['workItem'] as Record<string, unknown> | undefined;
    if (wi && typeof wi === 'object' && typeof wi['content'] === 'string') {
      return wi['content'];
    }
    return '';
  });

  protected readonly summary = computed<WorkItemSummary>(() => {
    const raw = this.artefact();
    if (!raw || typeof raw !== 'object') return {};
    const obj = raw as Record<string, unknown>;
    const s = obj['workItemSummary'];
    if (!s || typeof s !== 'object') return {};
    const sum = s as Record<string, unknown>;
    return {
      id: typeof sum['id'] === 'string' ? sum['id'] : undefined,
      type: typeof sum['type'] === 'string' ? sum['type'] : undefined,
      title: typeof sum['title'] === 'string' ? sum['title'] : undefined,
      path: typeof sum['path'] === 'string' ? sum['path'] : undefined,
    };
  });

  protected readonly canSubmit = computed(() =>
    this.canAct() && !this.working() && this.title().trim().length > 0);

  protected readonly canReject = computed(() =>
    this.canAct() && !this.working() && this.feedback().trim().length > 0);

  constructor() {
    effect(() => {
      const s = this.summary();
      if (s.title) this.title.set(s.title);
      const t = s.type;
      if (t === 'BUG' || t === 'IMP') this.type.set(t);
      else this.type.set('FEAT');
    });
  }

  protected onConfirm(): void {
    if (!this.canSubmit()) return;
    this.submitted.emit({
      outcome: 'confirmed',
      payload: { workItem: { title: this.title().trim(), type: this.type() } },
    });
  }

  protected onReject(): void {
    if (!this.canReject()) return;
    this.submitted.emit({
      outcome: 'rejected',
      payload: { feedback: this.feedback().trim() },
    });
  }

  protected toggleReject(): void {
    this.rejecting.update(v => !v);
    this.feedback.set('');
  }
}
