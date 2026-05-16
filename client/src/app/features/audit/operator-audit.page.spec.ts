import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OperatorAuditPage } from './operator-audit.page';

describe('OperatorAuditPage', () => {
  let mock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OperatorAuditPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    mock = TestBed.inject(HttpTestingController);
  });

  function flushAdmin(rows: { id: string; outcome: 'Granted' | 'Denied' | 'Failed' }[]) {
    const req = mock.expectOne(r => r.url === '/api/admin/audit');
    req.flush({
      data: rows.map(r => ({
        id: r.id, occurredAt: '2026-05-16T14:32:00Z',
        actingMember: { id: 'op', displayName: 'Operator' },
        targetType: 'WorkItem', targetId: '4f8c2a91d3e711ef',
        action: 'workitem:start', outcome: r.outcome,
      })),
      meta: { totalCount: rows.length, page: 1, pageSize: 50 },
    });
  }

  it('renders rows after the initial admin-audit load', async () => {
    const fixture = TestBed.createComponent(OperatorAuditPage);
    fixture.detectChanges();
    flushAdmin([{ id: 'a1', outcome: 'Granted' }, { id: 'a2', outcome: 'Denied' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const html = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(html).toContain('Audit log');
    expect(html).toContain('Granted');
    expect(html).toContain('Denied');
  });

  it('re-fetches when the filter changes', async () => {
    const fixture = TestBed.createComponent(OperatorAuditPage);
    fixture.detectChanges();
    flushAdmin([{ id: 'a1', outcome: 'Granted' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      onFilterChanged(f: { outcome?: string }): Promise<void>;
    };
    void cmp.onFilterChanged({ outcome: 'Denied' });
    await Promise.resolve();

    const second = mock.expectOne(r =>
      r.url === '/api/admin/audit' && r.params.get('outcome') === 'Denied');
    second.flush({
      data: [],
      meta: { totalCount: 0, page: 1, pageSize: 50 },
    });
    for (let i = 0; i < 3; i++) await Promise.resolve();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No audit entries match');
  });
});
