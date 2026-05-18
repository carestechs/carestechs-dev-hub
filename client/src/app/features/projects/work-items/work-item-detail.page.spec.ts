import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { WorkItemDetailPage } from './work-item-detail.page';

// Tiny EventSource stub that records constructions and lets us emit messages by hand.
class FakeEventSource {
  static instances: FakeEventSource[] = [];
  readonly url: string;
  onopen: (() => void) | null = null;
  onmessage: ((e: { data: string }) => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;

  constructor(url: string) {
    this.url = url;
    FakeEventSource.instances.push(this);
  }
  close(): void { this.closed = true; }
}

describe('WorkItemDetailPage', () => {
  let mock: HttpTestingController;
  let paramMap$: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let originalEventSource: any;

  beforeEach(async () => {
    originalEventSource = (globalThis as any).EventSource;
    (globalThis as any).EventSource = FakeEventSource;
    FakeEventSource.instances = [];

    paramMap$ = new BehaviorSubject(convertToParamMap({ slug: 'alpha', id: 'wi-1' }));
    await TestBed.configureTestingModule({
      imports: [WorkItemDetailPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: paramMap$.asObservable() } },
      ],
    }).compileComponents();
    mock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    (globalThis as any).EventSource = originalEventSource;
  });

  async function reconfigureWithAuth(isOperator: boolean): Promise<void> {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [WorkItemDetailPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: paramMap$.asObservable() } },
        { provide: AuthService, useValue: { isOperator: () => isOperator, token: () => null } },
      ],
    }).compileComponents();
    mock = TestBed.inject(HttpTestingController);
  }

  function flushProject(slug = 'alpha', id = 'p1', extras: Record<string, unknown> = {}) {
    mock.expectOne(`/api/projects/by-slug/${slug}`).flush({
      data: {
        id, name: 'Alpha', slug, projectType: 'feature-delivery',
        owningTeam: { id: 't', name: 'Engineering' },
        inFlightWorkItems: 0, createdAt: '2026-05-01T00:00:00Z',
        ...extras,
      },
    });
  }
  function flushWorkItem(
    projectId = 'p1', id = 'wi-1', executorKey = 'feature-delivery-v1',
    currentStatus = 'WaitingOnCheckpoint', extras: Record<string, unknown> = {},
  ) {
    mock.expectOne(`/api/projects/${projectId}/work-items/${id}`).flush({
      data: {
        id, projectId, title: 'Sample work', currentStatus, currentCheckpointKey: 'approve',
        executor: { id: 'e', key: executorKey, displayName: 'Feature Delivery v1' },
        executorCorrelationMarker: 'marker-1',
        createdAt: '2026-05-01T00:00:00Z',
        createdBy: { id: 'u', displayName: 'Op' },
        executorState: { step: 'approve' },
        ...extras,
      },
    });
  }
  function flushSignals(projectId = 'p1', workItemId = 'wi-1', items: { id: string; outcome: string }[] = []) {
    const req = mock.expectOne(r =>
      r.url === `/api/projects/${projectId}/work-items/${workItemId}/signals`);
    req.flush({
      data: items.map(i => ({
        id: i.id, checkpointKey: 'approve', outcome: i.outcome,
        signaledBy: { id: 'u', displayName: 'Beto' },
        signaledAt: '2026-05-01T00:00:00Z',
        executorResponseStatus: 200,
      })),
      meta: { totalCount: items.length, page: 1, pageSize: 20 },
    });
  }

  it('renders header + executor state + signal history after parallel load', async () => {
    TestBed.inject(AuthService); // pre-warm
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();

    flushProject();
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem();
    flushSignals('p1', 'wi-1', [{ id: 's1', outcome: 'approve' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Sample work');
    expect(html.textContent).toContain('marker-1');
    expect(html.textContent).toContain('WaitingOnCheckpoint');
    expect(html.textContent).toContain('feature-delivery-v1');
    expect(html.textContent).toContain('Beto'); // signal history
  });

  it('shows Open review CTA only for feature-delivery executor', async () => {
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject();
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem('p1', 'wi-1', 'totally-other-executor', 'Running');
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Open review');
  });

  it('renders forbidden page on 403', async () => {
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject();
    await Promise.resolve(); await Promise.resolve();
    mock.expectOne('/api/projects/p1/work-items/wi-1').flush({}, { status: 403, statusText: 'Forbidden' });
    // The signals call may race; if it was queued, the forbidden response above ends the parallel.
    mock.match(r => r.url === '/api/projects/p1/work-items/wi-1/signals').forEach(r => r.flush({}, { status: 403, statusText: 'Forbidden' }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain("You don't have access to this work item.");
  });

  it('does not open the stream when no access token is available', async () => {
    TestBed.inject(AuthService); // token defaults to null
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject();
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem();
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(FakeEventSource.instances.length).toBe(0);
  });

  // ---------- FEAT-008 / T-062: effective branch row + inline edit ----------

  it('renders the project default branch when work item has no override (FEAT-008)', async () => {
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject('alpha', 'p1', { defaultBranch: 'main' });
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem('p1', 'wi-1', 'feature-delivery-v1', 'WaitingOnCheckpoint', { workBranch: null });
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Branch:');
    expect(text).toContain('main');
    expect(text).toContain('project default');
  });

  it('renders the work item override when set, with override label (FEAT-008)', async () => {
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject('alpha', 'p1', { defaultBranch: 'main' });
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem('p1', 'wi-1', 'feature-delivery-v1', 'WaitingOnCheckpoint', { workBranch: 'feat/abc' });
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('feat/abc');
    expect(text).toContain('override');
  });

  it('renders "(not set)" when neither override nor project default exists', async () => {
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject(); // no defaultBranch
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem(); // no workBranch
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('(not set)');
  });

  it('operator sees the Edit button on the Branch row, non-operator does not', async () => {
    await reconfigureWithAuth(true);
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject('alpha', 'p1', { defaultBranch: 'main' });
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem('p1', 'wi-1', 'feature-delivery-v1', 'WaitingOnCheckpoint', { workBranch: null });
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    // Operator path — Edit button visible.
    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('button');
    const hasEdit = Array.from(buttons).some(b => (b.textContent ?? '').trim() === 'Edit');
    expect(hasEdit).withContext('operator should see the Branch row Edit button').toBe(true);
  });

  it('non-operator does not see the Edit button on the Branch row', async () => {
    // Default config — non-operator.
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject('alpha', 'p1', { defaultBranch: 'main' });
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem('p1', 'wi-1', 'feature-delivery-v1', 'WaitingOnCheckpoint', { workBranch: null });
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('button');
    const hasEdit = Array.from(buttons).some(b => (b.textContent ?? '').trim() === 'Edit');
    expect(hasEdit).withContext('non-operator should not see the Edit button').toBe(false);
  });

  // ---------- FEAT-009 / T-072: Assignments sidebar ----------

  it('Assignments section renders when executorState.assignments has entries, sorted by taskId', async () => {
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject();
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem('p1', 'wi-1', 'feature-delivery-v1', 'WaitingOnCheckpoint', {
      executorState: { assignments: { 'T-002': 'Bob', 'T-001': 'Alice' } },
    });
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    const html = (fixture.nativeElement as HTMLElement);
    expect(html.textContent).toContain('Assignments');
    // Two rows rendered.
    const items = html.querySelectorAll('ul.space-y-1 > li');
    expect(items.length).toBe(2);
    // Sorted ascending — T-001 row appears before T-002.
    expect(items[0].textContent).toContain('T-001');
    expect(items[0].textContent).toContain('Alice');
    expect(items[1].textContent).toContain('T-002');
    expect(items[1].textContent).toContain('Bob');
  });

  it('Assignments section is absent when executorState carries no assignments map', async () => {
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject();
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem('p1', 'wi-1', 'feature-delivery-v1', 'WaitingOnCheckpoint', {
      executorState: { step: 'approve' },  // no assignments key
    });
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    // The "Assignments" heading text should not appear anywhere on the page.
    expect(text).not.toContain('Assignments');
  });

  it('submitBranch sends PATCH and updates the page (FEAT-008)', async () => {
    await reconfigureWithAuth(true);
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject('alpha', 'p1', { defaultBranch: 'main' });
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem('p1', 'wi-1', 'feature-delivery-v1', 'WaitingOnCheckpoint', { workBranch: null });
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      openBranchEdit(): void;
      submitBranch(): Promise<void>;
      branchControl: { setValue: (v: string) => void };
    };
    cmp.openBranchEdit();
    cmp.branchControl.setValue('feat/abc');
    void cmp.submitBranch();
    await Promise.resolve();

    const patch = mock.expectOne(r =>
      r.url === '/api/projects/p1/work-items/wi-1' && r.method === 'PATCH');
    expect(patch.request.body).toEqual({ workBranch: 'feat/abc' });
    patch.flush({
      data: {
        id: 'wi-1', projectId: 'p1', title: 'Sample work', currentStatus: 'WaitingOnCheckpoint',
        currentCheckpointKey: 'approve',
        executor: { id: 'e', key: 'feature-delivery-v1', displayName: 'Feature Delivery v1' },
        executorCorrelationMarker: 'marker-1',
        createdAt: '2026-05-01T00:00:00Z',
        createdBy: { id: 'u', displayName: 'Op' },
        executorState: {},
        workBranch: 'feat/abc',
      },
    });
    for (let i = 0; i < 4; i++) await Promise.resolve();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('feat/abc');
    expect(text).toContain('override');
  });

  // ---------- FEAT-010 / T-087: executorRunId label ----------

  it('header shows the executorRunId next to the marker when set (FEAT-010)', async () => {
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject();
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem('p1', 'wi-1', 'feature-delivery-v1', 'WaitingOnCheckpoint', {
      executorRunId: '00000000-0000-0000-0000-000000000abc',
    });
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('marker:');
    expect(text).toContain('marker-1');
    expect(text).toContain('run:');
    expect(text).toContain('00000000-0000-0000-0000-000000000abc');
  });

  it('header omits the run label when executorRunId is absent', async () => {
    const fixture = TestBed.createComponent(WorkItemDetailPage);
    fixture.detectChanges();
    flushProject();
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem();  // no executorRunId in extras
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('run:');
  });
});
