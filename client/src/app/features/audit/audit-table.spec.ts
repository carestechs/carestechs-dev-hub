import { TestBed } from '@angular/core/testing';
import type { AuditEntryDto, AuditFilter } from '../../core/api/audit.types';
import { AuditTable } from './audit-table';

describe('AuditTable', () => {
  function render(rows: AuditEntryDto[], filter: AuditFilter = {}) {
    TestBed.configureTestingModule({ imports: [AuditTable] });
    const fixture = TestBed.createComponent(AuditTable);
    fixture.componentRef.setInput('rows', rows);
    fixture.componentRef.setInput('meta', { totalCount: rows.length, page: 1, pageSize: 50 });
    fixture.componentRef.setInput('filter', filter);
    fixture.detectChanges();
    return fixture;
  }

  function row(over: Partial<AuditEntryDto> = {}): AuditEntryDto {
    return {
      id: over.id ?? 'a1',
      occurredAt: over.occurredAt ?? '2026-05-16T14:32:00Z',
      actingMember: over.actingMember ?? { id: 'op', displayName: 'Operator' },
      projectId: over.projectId,
      targetType: over.targetType ?? 'WorkItem',
      targetId: over.targetId ?? '4f8c2a91d3e711ef',
      action: over.action ?? 'workitem:start',
      outcome: over.outcome ?? 'Granted',
      reason: over.reason,
      details: over.details,
    };
  }

  it('renders rows with the right outcome pill colors', () => {
    const fixture = render([
      row({ id: 'g', outcome: 'Granted' }),
      row({ id: 'd', outcome: 'Denied', reason: 'member is not on this project' }),
      row({ id: 'f', outcome: 'Failed', reason: 'executor failure' }),
    ]);
    const html = (fixture.nativeElement as HTMLElement);
    expect(html.textContent).toContain('Granted');
    expect(html.textContent).toContain('Denied');
    expect(html.textContent).toContain('Failed');
    expect(html.querySelector('.bg-emerald-50')).toBeTruthy();
    expect(html.querySelector('.bg-red-50')).toBeTruthy();
    expect(html.querySelector('.bg-amber-50')).toBeTruthy();
  });

  it('toggles details when a row is clicked', () => {
    const fixture = render([row({ id: 'x', details: { executorKey: 'feature-delivery-v1', marker: '4f8c' } })]);
    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).not.toContain('feature-delivery-v1');

    const cmp = fixture.componentInstance as unknown as { toggleExpand(id: string): void };
    cmp.toggleExpand('x');
    fixture.detectChanges();

    expect(html.textContent).toContain('feature-delivery-v1');
  });

  it('emits filterChanged when an outcome pill is clicked', () => {
    const fixture = render([row()]);
    const emits: AuditFilter[] = [];
    fixture.componentInstance.filterChanged.subscribe(f => emits.push(f));

    const cmp = fixture.componentInstance as unknown as { onOutcomeClick(o: string | null): void };
    cmp.onOutcomeClick('Denied');
    expect(emits.length).toBe(1);
    expect(emits[0].outcome).toBe('Denied');
  });

  it('renders the empty state with no rows', () => {
    const fixture = render([]);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No audit entries match');
  });
});
