import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, input, signal } from '@angular/core';
import { MarkdownRenderer } from './markdown-renderer';
import type { PanelSubmit } from './panel-submit';

interface BriefArtefact {
  work_item_id?: string;
  title?: string;
  summary?: string;
  source?: string;
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
  private initialized = false;

  protected readonly parsed = computed<BriefArtefact>(() => {
    const raw = this.artefact();
    if (raw && typeof raw === 'object') {
      const obj = raw as Record<string, unknown>;
      const result: BriefArtefact = {
        work_item_id: typeof obj['work_item_id'] === 'string' ? obj['work_item_id'] : undefined,
        title: typeof obj['title'] === 'string' ? obj['title'] : undefined,
        summary: typeof obj['summary'] === 'string' ? obj['summary'] : undefined,
        source: typeof obj['source'] === 'string' ? obj['source'] : undefined,
      };

      if (!this.initialized) {
        this.initialized = true;
        if (result.title) this.title.set(result.title);
        const id = result.work_item_id ?? '';
        if (id.startsWith('BUG')) this.type.set('BUG');
        else if (id.startsWith('IMP')) this.type.set('IMP');
        else this.type.set('FEAT');
      }

      return result;
    }
    return {};
  });

  protected readonly markdownSource = computed(() => {
    const p = this.parsed();
    return p.summary ?? p.source ?? '';
  });

  protected readonly canSubmit = computed(() =>
    this.canAct() && !this.working() && this.title().trim().length > 0);

  protected readonly canReject = computed(() =>
    this.canAct() && !this.working() && this.feedback().trim().length > 0);

  protected onConfirm(): void {
    if (!this.canSubmit()) return;
    this.submitted.emit({
      outcome: 'confirmed',
      payload: { title: this.title().trim(), type: this.type() },
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
