import { ChangeDetectionStrategy, Component, computed, EventEmitter, input, Output, TemplateRef } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import type { AppError } from '../../../core/errors/app-error';
import type { PageMeta } from '../../../core/api/workspace.types';
import { AppErrorBanner } from '../app-error-banner/app-error-banner';
import type { ColumnDef, PageChange, SortChange } from './app-table.types';

/**
 * Generic sortable + paginated table. Designed to be wrapped in <app-card>-like
 * shells when needed; the body owns the empty / loading / error states.
 */
@Component({
  selector: 'app-table',
  standalone: true,
  imports: [NgTemplateOutlet, AppErrorBanner],
  templateUrl: './app-table.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppTable<TRow> {
  readonly columns = input.required<ColumnDef<TRow>[]>();
  readonly rows = input.required<TRow[]>();
  readonly meta = input<PageMeta | null>(null);
  readonly loading = input<boolean>(false);
  readonly error = input<AppError | null>(null);
  readonly trackBy = input<((index: number, row: TRow) => unknown) | null>(null);
  readonly emptyTitle = input<string>('Nothing here yet.');
  readonly emptyDescription = input<string | null>(null);

  @Output() readonly sortChanged = new EventEmitter<SortChange>();
  @Output() readonly pageChanged = new EventEmitter<PageChange>();
  @Output() readonly rowClicked = new EventEmitter<TRow>();

  protected readonly hasError = computed(() => this.error() != null);
  protected readonly isEmpty = computed(() => !this.loading() && !this.hasError() && this.rows().length === 0);
  protected readonly hasRows = computed(() => !this.loading() && !this.hasError() && this.rows().length > 0);

  protected readonly totalPages = computed(() => {
    const m = this.meta();
    if (!m || m.pageSize <= 0) return 1;
    return Math.max(1, Math.ceil(m.totalCount / m.pageSize));
  });
  protected readonly currentPage = computed(() => this.meta()?.page ?? 1);
  protected readonly hasPrev = computed(() => this.currentPage() > 1);
  protected readonly hasNext = computed(() => this.currentPage() < this.totalPages());

  protected readonly rangeLabel = computed(() => {
    const m = this.meta();
    if (!m || m.totalCount === 0) return '';
    const start = (m.page - 1) * m.pageSize + 1;
    const end = Math.min(m.totalCount, m.page * m.pageSize);
    return `${start}–${end} of ${m.totalCount}`;
  });

  protected onHeaderClick(col: ColumnDef<TRow>): void {
    if (!col.sortable) return;
    const meta = this.meta();
    const currentDir = meta?.sortBy === col.id ? meta?.sortDir : undefined;
    const nextDir: 'asc' | 'desc' = currentDir === 'asc' ? 'desc' : 'asc';
    this.sortChanged.emit({ sortBy: col.id, sortDir: nextDir });
  }

  protected onPrev(): void {
    if (!this.hasPrev()) return;
    const m = this.meta();
    if (!m) return;
    this.pageChanged.emit({ page: Math.max(1, m.page - 1), pageSize: m.pageSize });
  }

  protected onNext(): void {
    if (!this.hasNext()) return;
    const m = this.meta();
    if (!m) return;
    this.pageChanged.emit({ page: Math.min(this.totalPages(), m.page + 1), pageSize: m.pageSize });
  }

  protected sortIndicator(col: ColumnDef<TRow>): string | null {
    if (!col.sortable) return null;
    const m = this.meta();
    if (!m || m.sortBy !== col.id) return null;
    return m.sortDir === 'asc' ? '↑' : '↓';
  }

  protected isTemplate(cell: ColumnDef<TRow>['cell']): cell is TemplateRef<{ $implicit: TRow }> {
    return cell instanceof TemplateRef;
  }

  protected renderCell(cell: ColumnDef<TRow>['cell'], row: TRow): string {
    if (typeof cell === 'function') {
      const v = cell(row);
      return v == null ? '' : String(v);
    }
    return '';
  }

  protected templateOf(cell: ColumnDef<TRow>['cell']): TemplateRef<{ $implicit: TRow }> | null {
    return cell instanceof TemplateRef ? cell : null;
  }

  protected trackByFn = (index: number, row: TRow): unknown => {
    const fn = this.trackBy();
    return fn ? fn(index, row) : index;
  };

  protected alignClass(align: ColumnDef<TRow>['align'] | undefined): string {
    return align === 'right' ? 'text-right'
         : align === 'center' ? 'text-center'
         : 'text-left';
  }
}
