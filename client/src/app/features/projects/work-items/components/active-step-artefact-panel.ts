import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ArtefactFallback } from './artefact-fallback';
import { DiffViewer } from './diff-viewer';
import { MarkdownRenderer } from './markdown-renderer';

type ArtefactKind = 'diff' | 'markdown' | 'fallback';

@Component({
  selector: 'active-step-artefact-panel',
  standalone: true,
  imports: [DiffViewer, MarkdownRenderer, ArtefactFallback],
  template: `
    @switch (kind()) {
      @case ('diff')     { <diff-viewer [lines]="diffLines()" /> }
      @case ('markdown') { <markdown-renderer [source]="markdownSource()" /> }
      @default            { <artefact-fallback [artefact]="artefact()" /> }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActiveStepArtefactPanel {
  readonly artefact = input<unknown>(null);

  protected readonly kind = computed<ArtefactKind>(() => {
    const a = this.artefact();
    if (a === null || a === undefined || typeof a !== 'object') return 'fallback';
    const k = (a as { kind?: string }).kind;
    if (k === 'diff' || k === 'markdown') return k;
    return 'fallback';
  });

  protected readonly diffLines = computed(() => {
    const a = this.artefact() as { lines?: unknown; added?: string[]; removed?: string[]; context?: string[] };
    if (Array.isArray(a?.lines)) return a.lines as never;
    return { added: a?.added ?? [], removed: a?.removed ?? [], context: a?.context ?? [] };
  });

  protected readonly markdownSource = computed(() => {
    const a = this.artefact() as { source?: string; body?: string };
    return a?.source ?? a?.body ?? '';
  });
}
