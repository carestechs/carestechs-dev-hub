import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { ReviewPage } from './review.page';

class FakeEventSource {
  static instances: FakeEventSource[] = [];
  readonly url: string;
  onopen: (() => void) | null = null;
  onmessage: ((e: { data: string }) => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;
  constructor(url: string) { this.url = url; FakeEventSource.instances.push(this); }
  close(): void { this.closed = true; }
}

describe('ReviewPage', () => {
  let mock: HttpTestingController;
  let paramMap$: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let originalEventSource: any;

  beforeEach(async () => {
    originalEventSource = (globalThis as any).EventSource;
    (globalThis as any).EventSource = FakeEventSource;
    FakeEventSource.instances = [];

    paramMap$ = new BehaviorSubject(convertToParamMap({ slug: 'alpha', id: 'wi-1' }));
    await TestBed.configureTestingModule({
      imports: [ReviewPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: paramMap$.asObservable() } },
      ],
    }).compileComponents();
    mock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => { (globalThis as any).EventSource = originalEventSource; });

  function flushProject() {
    mock.expectOne('/api/projects/by-slug/alpha').flush({
      data: {
        id: 'p1', name: 'Alpha', slug: 'alpha', projectType: 'feature-delivery',
        owningTeam: { id: 't', name: 'Engineering' },
        inFlightWorkItems: 0, createdAt: '2026-05-01T00:00:00Z',
      },
    });
  }
  function flushWorkItem(state: any = { steps: [{ key: 'brief', label: 'Brief' }, { key: 'approve', label: 'Approve' }] }, currentCheckpointKey: string | null = 'approve', currentStatus = 'WaitingOnCheckpoint') {
    mock.expectOne('/api/projects/p1/work-items/wi-1').flush({
      data: {
        id: 'wi-1', projectId: 'p1', title: 'Sample', currentStatus, currentCheckpointKey,
        executor: { id: 'e', key: 'feature-delivery-v1', displayName: 'Feature Delivery v1' },
        executorCorrelationMarker: 'marker-1',
        createdAt: '2026-05-01T00:00:00Z',
        createdBy: { id: 'u', displayName: 'Op' },
        executorState: state,
      },
    });
  }
  function flushSignals(items: { key: string; outcome: string }[] = []) {
    mock.expectOne(r => r.url === '/api/projects/p1/work-items/wi-1/signals').flush({
      data: items.map((i, idx) => ({
        id: `s${idx}`, checkpointKey: i.key, outcome: i.outcome,
        signaledBy: { id: 'u', displayName: 'Ana' },
        signaledAt: '2026-05-01T00:00:00Z',
        executorResponseStatus: 200,
      })),
      meta: { totalCount: items.length, page: 1, pageSize: 50 },
    });
  }
  function flushContract() {
    mock.expectOne('/api/projects/p1/work-items/wi-1/checkpoints/approve').flush({
      data: {
        checkpointKey: 'approve', displayName: 'Approve',
        requiredRoleKey: 'reviewer', allowedOutcomes: ['approve', 'reject'],
        state: 'active',
      },
    });
  }

  it('renders the timeline and the action bar in read-only mode when the caller lacks the role', async () => {
    const fixture = TestBed.createComponent(ReviewPage);
    fixture.detectChanges();
    flushProject();
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem();
    flushSignals([{ key: 'brief', outcome: 'approve' }]);
    for (let i = 0; i < 4; i++) await Promise.resolve();
    flushContract();
    for (let i = 0; i < 4; i++) await Promise.resolve();
    await fixture.whenStable();
    fixture.detectChanges();

    const html = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(html).toContain('Brief');
    expect(html).toContain('Approve'); // active step rendered
    expect(html).toContain('Ana'); // decision history
    // Caller lacks reviewer role → read-only message rendered.
    expect(html).toContain('This step is waiting on role:');
    expect(html).toContain('reviewer');
  });

  it('shows the completed banner when the work item is in a terminal status', async () => {
    const fixture = TestBed.createComponent(ReviewPage);
    fixture.detectChanges();
    flushProject();
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem({ steps: [{ key: 'brief', label: 'Brief' }] }, null, 'Completed');
    flushSignals([{ key: 'brief', outcome: 'approve' }]);
    // Completed status → no contract fetch.
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No active checkpoint.');
  });

  it('falls back to a friendly message when executorState has no steps', async () => {
    const fixture = TestBed.createComponent(ReviewPage);
    fixture.detectChanges();
    flushProject();
    await Promise.resolve(); await Promise.resolve();
    flushWorkItem({}, null, 'Running');
    flushSignals();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No timeline available');
  });
});
