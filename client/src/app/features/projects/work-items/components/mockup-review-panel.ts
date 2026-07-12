import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, inject, signal, input } from '@angular/core';
import { DomSanitizer, type SafeHtml } from '@angular/platform-browser';
import type { PanelSubmit } from './panel-submit';

interface CurrentTask {
  id?: string;
  title?: string;
  description?: string;
}

@Component({
  selector: 'mockup-review-panel',
  standalone: true,
  templateUrl: './mockup-review-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MockupReviewPanel {
  readonly artefact = input<unknown>(null);
  readonly canAct = input<boolean>(false);
  readonly working = input<boolean>(false);

  @Output() readonly submitted = new EventEmitter<PanelSubmit>();

  private readonly sanitizer = inject(DomSanitizer);

  protected readonly requesting = signal(false);
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
      description: typeof t['description'] === 'string' ? t['description'] : undefined,
    };
  });

  protected readonly mockupHtml = computed<SafeHtml | null>(() => {
    const raw = this.artefact();
    if (!raw || typeof raw !== 'object') return null;
    const obj = raw as Record<string, unknown>;
    const html = typeof obj['mockupHtml'] === 'string' ? obj['mockupHtml'] : '';
    return html ? this.sanitizer.bypassSecurityTrustHtml(html) : null;
  });

  protected readonly mockupDescription = computed(() => {
    const raw = this.artefact();
    if (!raw || typeof raw !== 'object') return '';
    const obj = raw as Record<string, unknown>;
    return typeof obj['mockupDescription'] === 'string' ? obj['mockupDescription'] : '';
  });

  protected readonly canApprove = computed(() => this.canAct() && !this.working());

  protected readonly canRequestChanges = computed(() =>
    this.canAct() && !this.working() && this.feedback().trim().length > 0);

  protected onApprove(): void {
    if (!this.canApprove()) return;
    this.submitted.emit({ outcome: 'approve', payload: {} });
  }

  protected onRequestChanges(): void {
    if (!this.canRequestChanges()) return;
    this.submitted.emit({
      outcome: 'reject',
      payload: { feedback: this.feedback().trim() },
    });
  }

  protected toggleRequest(): void {
    this.requesting.update(v => !v);
    this.feedback.set('');
  }
}
