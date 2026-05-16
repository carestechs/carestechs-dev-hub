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

  function flushProject(slug = 'alpha', id = 'p1') {
    mock.expectOne(`/api/projects/by-slug/${slug}`).flush({
      data: {
        id, name: 'Alpha', slug, projectType: 'feature-delivery',
        owningTeam: { id: 't', name: 'Engineering' },
        inFlightWorkItems: 0, createdAt: '2026-05-01T00:00:00Z',
      },
    });
  }
  function flushWorkItem(projectId = 'p1', id = 'wi-1', executorKey = 'feature-delivery-v1', currentStatus = 'WaitingOnCheckpoint') {
    mock.expectOne(`/api/projects/${projectId}/work-items/${id}`).flush({
      data: {
        id, projectId, title: 'Sample work', currentStatus, currentCheckpointKey: 'approve',
        executor: { id: 'e', key: executorKey, displayName: 'Feature Delivery v1' },
        executorCorrelationMarker: 'marker-1',
        createdAt: '2026-05-01T00:00:00Z',
        createdBy: { id: 'u', displayName: 'Op' },
        executorState: { step: 'approve' },
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
});
