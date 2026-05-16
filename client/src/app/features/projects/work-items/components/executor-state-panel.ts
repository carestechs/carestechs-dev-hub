import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

interface FlatEntry { key: string; value: unknown; isObject: boolean; }

@Component({
  selector: 'executor-state-panel',
  standalone: true,
  templateUrl: './executor-state-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ExecutorStatePanel {
  readonly state = input<unknown>(null);

  protected readonly expanded = signal<Set<string>>(new Set());

  protected readonly entries = computed<FlatEntry[]>(() => {
    const s = this.state();
    if (s === null || s === undefined) return [];
    if (typeof s !== 'object' || Array.isArray(s)) {
      return [{ key: '(value)', value: s, isObject: false }];
    }
    return Object.entries(s as Record<string, unknown>).map(([k, v]) => ({
      key: k,
      value: v,
      isObject: v !== null && typeof v === 'object',
    }));
  });

  protected isExpanded(key: string): boolean {
    return this.expanded().has(key);
  }
  protected toggle(key: string): void {
    this.expanded.update(set => {
      const next = new Set(set);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  protected isEmpty(): boolean {
    const s = this.state();
    if (s === null || s === undefined) return true;
    if (typeof s === 'object' && !Array.isArray(s)) return Object.keys(s as object).length === 0;
    return false;
  }

  protected formatPrimitive(v: unknown): string {
    if (v === null) return 'null';
    if (v === undefined) return '(undefined)';
    if (typeof v === 'string') return v;
    return JSON.stringify(v);
  }

  protected childCount(value: unknown): number {
    if (Array.isArray(value)) return value.length;
    if (value !== null && typeof value === 'object') return Object.keys(value as object).length;
    return 0;
  }

  protected childLabel(value: unknown): string {
    return Array.isArray(value) ? 'items' : 'fields';
  }

  protected pretty(value: unknown): string {
    return JSON.stringify(value, null, 2);
  }
}
