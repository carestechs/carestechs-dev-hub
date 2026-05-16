import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { PageMeta } from '../../../core/api/workspace.types';
import { AppTable } from './app-table';
import type { ColumnDef } from './app-table.types';

interface TestRow { id: string; name: string; count: number; }

@Component({
  standalone: true,
  imports: [AppTable],
  template: `
    <app-table
      [columns]="columns"
      [rows]="rows"
      [meta]="meta"
      [loading]="loading"
      [error]="error"
      emptyTitle="No items."
      (sortChanged)="lastSort = $event"
      (pageChanged)="lastPage = $event"
      (rowClicked)="clicked.push($event)"
    />
  `,
})
class Host {
  columns: ColumnDef<TestRow>[] = [
    { id: 'name',  header: 'Name',  cell: r => r.name,  sortable: true },
    { id: 'count', header: 'Count', cell: r => r.count, align: 'right' },
  ];
  rows: TestRow[] = [];
  meta: PageMeta | null = null;
  loading = false;
  error: import('../../../core/errors/app-error').AppError | null = null;
  lastSort: unknown;
  lastPage: unknown;
  clicked: TestRow[] = [];
}

describe('AppTable', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
  });

  it('renders rows with cell values', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.rows = [
      { id: 'a', name: 'Alpha', count: 1 },
      { id: 'b', name: 'Beta',  count: 2 },
    ];
    fixture.detectChanges();
    const cells = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody td');
    expect(cells[0].textContent?.trim()).toContain('Alpha');
    expect(cells[1].textContent?.trim()).toContain('1');
  });

  it('shows skeleton rows when loading', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.loading = true;
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.animate-pulse').length).toBeGreaterThan(0);
  });

  it('shows empty state when no rows and not loading', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No items.');
  });

  it('shows the error banner when an error is set', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.error = { type: '/probs/internal', title: 'Boom', status: 500 };
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role=alert]')?.textContent).toContain('Boom');
  });

  it('emits sortChanged with toggled direction', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.rows = [{ id: 'a', name: 'A', count: 1 }];
    fixture.componentInstance.meta = { totalCount: 1, page: 1, pageSize: 10, sortBy: 'name', sortDir: 'asc' };
    fixture.detectChanges();

    const nameHeader = (fixture.nativeElement as HTMLElement).querySelector('th') as HTMLTableCellElement;
    nameHeader.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.lastSort).toEqual({ sortBy: 'name', sortDir: 'desc' });
  });

  it('emits pageChanged via Next button', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.rows = [{ id: 'a', name: 'A', count: 1 }];
    fixture.componentInstance.meta = { totalCount: 30, page: 1, pageSize: 10 };
    fixture.detectChanges();
    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('tfoot button, button');
    const next = Array.from(buttons).find(b => b.textContent?.includes('Next')) as HTMLButtonElement;
    next.click();
    expect(fixture.componentInstance.lastPage).toEqual({ page: 2, pageSize: 10 });
  });

  it('emits rowClicked on row click', () => {
    const fixture = TestBed.createComponent(Host);
    const row = { id: 'a', name: 'Alpha', count: 1 };
    fixture.componentInstance.rows = [row];
    fixture.detectChanges();
    const tr = (fixture.nativeElement as HTMLElement).querySelector('tbody tr') as HTMLTableRowElement;
    tr.click();
    expect(fixture.componentInstance.clicked).toEqual([row]);
  });
});
