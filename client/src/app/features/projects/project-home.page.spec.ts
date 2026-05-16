import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { ProjectHomePage } from './project-home.page';

describe('ProjectHomePage', () => {
  let mock: HttpTestingController;
  let paramMap$: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  beforeEach(async () => {
    paramMap$ = new BehaviorSubject(convertToParamMap({ slug: 'alpha' }));
    await TestBed.configureTestingModule({
      imports: [ProjectHomePage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: paramMap$.asObservable() } },
      ],
    }).compileComponents();
    mock = TestBed.inject(HttpTestingController);
  });

  function flushProject(id = 'a', slug = 'alpha') {
    mock.expectOne(`/api/projects/by-slug/${slug}`).flush({
      data: {
        id, name: slug.toUpperCase(), slug, projectType: 'feature-delivery',
        owningTeam: { id: 't', name: 'Engineering' },
        inFlightWorkItems: 0, createdAt: '2026-05-01T00:00:00Z',
      },
    });
  }
  function flushWorkItems(projectId: string, items: { id: string; title: string; currentStatus: string }[] = []) {
    const req = mock.expectOne(r => r.url === `/api/projects/${projectId}/work-items`);
    req.flush({
      data: items.map(i => ({
        id: i.id, projectId, title: i.title, currentStatus: i.currentStatus,
        executor: { id: 'e', key: 'feature-delivery-v1', displayName: 'Feature Delivery v1' },
        executorCorrelationMarker: 'm1',
        createdAt: '2026-05-01T00:00:00Z',
        createdBy: { id: 'u', displayName: 'Op' },
      })),
      meta: { totalCount: items.length, page: 1, pageSize: 20 },
    });
  }

  it('renders the header and the work items table after a successful load', async () => {
    const fixture = TestBed.createComponent(ProjectHomePage);
    fixture.detectChanges();

    flushProject('p1', 'alpha');
    await Promise.resolve(); await Promise.resolve();
    flushWorkItems('p1', [
      { id: 'w1', title: 'Add CSV export', currentStatus: 'WaitingOnCheckpoint' },
      { id: 'w2', title: 'Migrate v2', currentStatus: 'Running' },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('h1')?.textContent).toContain('ALPHA');
    expect(html.textContent).toContain('Engineering');
    expect(html.textContent).toContain('Add CSV export');
    expect(html.textContent).toContain('Migrate v2');
    expect(html.textContent).toContain('Start work →');
  });

  it('renders the empty state when no work items exist', async () => {
    const fixture = TestBed.createComponent(ProjectHomePage);
    fixture.detectChanges();
    flushProject('p1', 'alpha');
    await Promise.resolve(); await Promise.resolve();
    flushWorkItems('p1', []);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No work items yet.');
  });

  it('starts work via the modal and refreshes the list', async () => {
    const fixture = TestBed.createComponent(ProjectHomePage);
    fixture.detectChanges();
    flushProject('p1', 'alpha');
    await Promise.resolve(); await Promise.resolve();
    flushWorkItems('p1', []);
    await fixture.whenStable();
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      openStart(): void;
      onStartSubmitted(r: { title: string; input: unknown }): Promise<void>;
    };
    cmp.openStart();
    fixture.detectChanges();
    void cmp.onStartSubmitted({ title: 'My work', input: {} });
    await Promise.resolve();

    mock.expectOne(r => r.url === '/api/projects/p1/work-items' && r.method === 'POST').flush({
      data: {
        id: 'w1', projectId: 'p1', title: 'My work', currentStatus: 'Running',
        executor: { id: 'e', key: 'feature-delivery-v1', displayName: 'Feature Delivery v1' },
        executorCorrelationMarker: 'm1',
        createdAt: '2026-05-01T00:00:00Z',
        createdBy: { id: 'u', displayName: 'Op' },
        executorState: {},
      },
    });
    for (let i = 0; i < 6; i++) await Promise.resolve();

    flushWorkItems('p1', [{ id: 'w1', title: 'My work', currentStatus: 'Running' }]);
    for (let i = 0; i < 4; i++) await Promise.resolve();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('My work');
  });

  it('renders "Project not found." on 404', async () => {
    const fixture = TestBed.createComponent(ProjectHomePage);
    fixture.detectChanges();

    mock.expectOne('/api/projects/by-slug/alpha')
       .flush({}, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Project not found.');
  });

  it('renders the forbidden state on 403', async () => {
    const fixture = TestBed.createComponent(ProjectHomePage);
    fixture.detectChanges();

    mock.expectOne('/api/projects/by-slug/alpha')
       .flush({}, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain("You don't have access to this project.");
  });

  it('reloads when the slug param changes', async () => {
    const fixture = TestBed.createComponent(ProjectHomePage);
    fixture.detectChanges();
    flushProject('a', 'alpha');
    await Promise.resolve(); await Promise.resolve();
    flushWorkItems('a', []);
    await fixture.whenStable();

    paramMap$.next(convertToParamMap({ slug: 'beta' }));
    fixture.detectChanges();
    for (let i = 0; i < 4; i++) await Promise.resolve();
    flushProject('b', 'beta');
    for (let i = 0; i < 4; i++) await Promise.resolve();
    flushWorkItems('b', []);
    for (let i = 0; i < 4; i++) await Promise.resolve();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('BETA');
  });
});
