import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

@Component({
  selector: 'artefact-fallback',
  standalone: true,
  template: `
    @if (pretty()) {
      <pre class="bg-slate-50 rounded-lg p-3 text-xs font-mono overflow-auto">{{ pretty() }}</pre>
    } @else {
      <p class="text-sm text-slate-500">No artefact for this step.</p>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ArtefactFallback {
  readonly artefact = input<unknown>(null);
  protected readonly pretty = computed(() => {
    const v = this.artefact();
    if (v === null || v === undefined) return '';
    return JSON.stringify(v, null, 2);
  });
}
