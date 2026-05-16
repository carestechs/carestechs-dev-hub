import type { TemplateRef } from '@angular/core';

export interface ColumnDef<TRow> {
  id: string;
  header: string;
  /** Either a plain cell renderer (string from the row) or a named template ref. */
  cell: ((row: TRow) => string | number | null | undefined) | TemplateRef<{ $implicit: TRow }>;
  sortable?: boolean;
  align?: 'left' | 'right' | 'center';
  widthClass?: string;
}

export interface SortChange {
  sortBy: string;
  sortDir: 'asc' | 'desc';
}

export interface PageChange {
  page: number;
  pageSize: number;
}
