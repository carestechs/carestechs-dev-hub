import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Injectable, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { PendingActionDto } from '../../core/api/notifications.types';
import { PendingActionsStore } from '../../core/notifications/pending-actions.store';
import { OperatorDashboardPage } from './operator-dashboard.page';

/** Minimal stand-in for the real store so tests own pendingActions state explicitly. */
@Injectable()
class StubPendingActionsStore {
  list = signal<PendingActionDto[]>([]);
  count = signal(0);
  badgeText = signal<string | null>(null);
  connected = signal(true);
  loading = signal(false);
  refresh = jasmine.createSpy('refresh').and.resolveTo();
  reconnect = jasmine.createSpy('reconnect');
  shutdown = jasmine.createSpy('shutdown');
  start = jasmine.createSpy('start').and.resolveTo();
}

describe('OperatorDashboardPage', () => {
  let mock: HttpTestingController;
  let stub: StubPendingActionsStore;

  beforeEach(async () => {
    stub = new StubPendingActionsStore();
    await TestBed.configureTestingModule({
      imports: [OperatorDashboardPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PendingActionsStore, useValue: stub },
      ],
    }).compileComponents();
    mock = TestBed.inject(HttpTestingController);
  });

  function flushProjects(projects: { id: string; slug: string; inFlight: number }[]) {
    const req = mock.expectOne(r => r.url === '/api/projects');
    req.flush({
      data: projects.map(p => ({
        id: p.id, name: p.slug, slug: p.slug, projectType: 'feature-delivery',
        owningTeam: { id: 't', name: 'Eng' },
        inFlightWorkItems: p.inFlight,
        createdAt: '2026-05-01T00:00:00Z',
      })),
      meta: { totalCount: projects.length, page: 1, pageSize: 100 },
    });
  }

  function flushAudit(entries: { id: string; outcome: 'Granted' | 'Denied' | 'Failed' }[]) {
    const req = mock.expectOne(r => r.url === '/api/admin/audit');
    req.flush({
      data: entries.map(e => ({
        id: e.id, occurredAt: '2026-05-16T14:00:00Z',
        actingMember: { id: 'op', displayName: 'Operator' },
        targetType: 'WorkItem', targetId: '4f8c2a91d3e711ef',
        action: 'workitem:start', outcome: e.outcome,
        reason: e.outcome === 'Granted' ? null : 'sample reason',
      })),
      meta: { totalCount: entries.length, page: 1, pageSize: 50 },
    });
  }

  it('renders all three panels after parallel load', async () => {
    stub.list.set([
      {
        projectId: 'p1', projectSlug: 'alpha',
        workItemId: 'w1', workItemTitle: 'Add CSV export',
        checkpointKey: 'approve', checkpointDisplayName: 'Approve',
        raisedAt: '2026-05-16T10:00:00Z',
      },
    ]);
    stub.count.set(1);

    const fixture = TestBed.createComponent(OperatorDashboardPage);
    fixture.detectChanges();

    flushProjects([
      { id: 'p1', slug: 'alpha', inFlight: 3 },
      { id: 'p2', slug: 'beta', inFlight: 0 },
    ]);
    flushAudit([
      { id: 'a1', outcome: 'Granted' },
      { id: 'a2', outcome: 'Denied' },
      { id: 'a3', outcome: 'Failed' },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    const html = (fixture.nativeElement as HTMLElement).textContent ?? '';
    // In-flight totals panel: sum across loaded projects = 3.
    expect(html).toContain('Operator dashboard');
    expect(html).toContain('3');
    expect(html).toContain('alpha');
    // beta has 0 in-flight and should NOT appear in the per-project breakdown.
    expect(html).not.toContain('beta');

    // Pending panel reads from the store stub.
    expect(html).toContain('Pending on you');
    expect(html).toContain('Add CSV export');

    // Recent events panel client-filters to Denied + Failed.
    expect(html).toContain('Denied');
    expect(html).toContain('Failed');
    // Granted rows are filtered out.
    expect(html).not.toMatch(/Granted/);
  });

  it('shows "Nothing in flight" empty state when all projects have zero in-flight work', async () => {
    const fixture = TestBed.createComponent(OperatorDashboardPage);
    fixture.detectChanges();

    flushProjects([{ id: 'p1', slug: 'alpha', inFlight: 0 }]);
    flushAudit([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const html = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(html).toContain('Nothing in flight.');
  });

  it('refresh triggers a re-fetch of projects + audit + pending store', async () => {
    const fixture = TestBed.createComponent(OperatorDashboardPage);
    fixture.detectChanges();
    flushProjects([{ id: 'p1', slug: 'alpha', inFlight: 1 }]);
    flushAudit([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as { refresh(): Promise<void> };
    const initialRefreshCalls = stub.refresh.calls.count();

    void cmp.refresh();
    await Promise.resolve();

    flushProjects([{ id: 'p1', slug: 'alpha', inFlight: 2 }]);
    flushAudit([]);
    for (let i = 0; i < 4; i++) await Promise.resolve();
    await fixture.whenStable();

    expect(stub.refresh.calls.count()).toBeGreaterThan(initialRefreshCalls);
  });
});
