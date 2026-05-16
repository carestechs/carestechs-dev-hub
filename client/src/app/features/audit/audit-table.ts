import { ChangeDetectionStrategy, Component, computed, EventEmitter, input, Output, signal } from '@angular/core';
import type { PageMeta } from '../../core/api/workspace.types';
import type { AuditEntryDto, AuditFilter, AuditOutcome } from '../../core/api/audit.types';

const OUTCOME_FILTER_OPTIONS: { label: string; value: AuditOutcome | null }[] = [
  { label: 'All', value: null },
  { label: 'Granted', value: 'Granted' },
  { label: 'Denied', value: 'Denied' },
  { label: 'Failed', value: 'Failed' },
];

@Component({
  selector: 'audit-table',
  standalone: true,
  templateUrl: './audit-table.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditTable {
  readonly rows = input<AuditEntryDto[]>([]);
  readonly loading = input<boolean>(false);
  readonly meta = input<PageMeta | null>(null);
  readonly filter = input<AuditFilter>({});
  readonly showFilters = input<boolean>(true);

  @Output() readonly filterChanged = new EventEmitter<AuditFilter>();
  @Output() readonly pageChanged = new EventEmitter<{ page: number; pageSize: number }>();

  protected readonly outcomeOptions = OUTCOME_FILTER_OPTIONS;
  protected readonly expandedIds = signal<Set<string>>(new Set());

  // Debounce action-input changes by 250ms.
  private actionDebounceTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly totalPages = computed(() => {
    const m = this.meta();
    if (!m) return 1;
    return Math.max(1, Math.ceil(m.totalCount / m.pageSize));
  });

  protected isExpanded(id: string): boolean { return this.expandedIds().has(id); }

  protected toggleExpand(id: string): void {
    this.expandedIds.update(s => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  protected outcomePillClass(outcome: AuditOutcome): string {
    switch (outcome) {
      case 'Granted': return 'bg-emerald-50 text-emerald-700';
      case 'Denied':  return 'bg-red-50 text-red-700';
      case 'Failed':  return 'bg-amber-50 text-amber-800';
    }
  }

  protected onActionInput(ev: Event): void {
    const value = (ev.target as HTMLInputElement).value;
    if (this.actionDebounceTimer) clearTimeout(this.actionDebounceTimer);
    this.actionDebounceTimer = setTimeout(() => {
      this.filterChanged.emit({ ...this.filter(), action: value || undefined });
    }, 250);
  }

  protected onOutcomeClick(outcome: AuditOutcome | null): void {
    this.filterChanged.emit({ ...this.filter(), outcome: outcome ?? undefined });
  }

  protected onFromChange(ev: Event): void {
    const value = (ev.target as HTMLInputElement).value;
    this.filterChanged.emit({ ...this.filter(), from: value ? new Date(value).toISOString() : undefined });
  }

  protected onToChange(ev: Event): void {
    const value = (ev.target as HTMLInputElement).value;
    this.filterChanged.emit({ ...this.filter(), to: value ? new Date(value).toISOString() : undefined });
  }

  protected clearFilters(): void {
    this.filterChanged.emit({});
  }

  protected onPrev(): void {
    const m = this.meta();
    if (!m || m.page <= 1) return;
    this.pageChanged.emit({ page: m.page - 1, pageSize: m.pageSize });
  }

  protected onNext(): void {
    const m = this.meta();
    if (!m) return;
    const total = this.totalPages();
    if (m.page >= total) return;
    this.pageChanged.emit({ page: m.page + 1, pageSize: m.pageSize });
  }

  protected formatDetails(details: unknown): string {
    if (details === null || details === undefined) return '(no details)';
    try { return JSON.stringify(details, null, 2); }
    catch { return String(details); }
  }

  protected shortId(value: string | undefined): string {
    if (!value) return '—';
    return value.length > 8 ? `${value.slice(0, 8)}…` : value;
  }
}
