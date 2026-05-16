import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export interface DiffLine { kind: 'added' | 'removed' | 'context'; text: string; }

@Component({
  selector: 'diff-viewer',
  standalone: true,
  templateUrl: './diff-viewer.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DiffViewer {
  /**
   * Either:
   *  - a pre-shaped DiffLine[] (preferred), or
   *  - a {added: string[], removed: string[], context: string[]} blob from the executor.
   */
  readonly lines = input<DiffLine[] | { added?: string[]; removed?: string[]; context?: string[] }>([]);

  protected readonly resolved = computed<DiffLine[]>(() => {
    const v = this.lines();
    if (Array.isArray(v)) return v;
    const out: DiffLine[] = [];
    for (const t of v.added ?? []) out.push({ kind: 'added', text: t });
    for (const t of v.removed ?? []) out.push({ kind: 'removed', text: t });
    for (const t of v.context ?? []) out.push({ kind: 'context', text: t });
    return out;
  });

  protected classFor(kind: DiffLine['kind']): string {
    switch (kind) {
      case 'added':   return 'bg-emerald-50 text-emerald-800';
      case 'removed': return 'bg-red-50 text-red-700';
      default:        return 'text-slate-500';
    }
  }
  protected prefixFor(kind: DiffLine['kind']): string {
    return kind === 'added' ? '+' : kind === 'removed' ? '-' : ' ';
  }
}
