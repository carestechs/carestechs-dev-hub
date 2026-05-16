import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { MembershipsPage } from './memberships.page';

describe('MembershipsPage', () => {
  let mock: HttpTestingController;
  let paramMap$: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  beforeEach(async () => {
    paramMap$ = new BehaviorSubject(convertToParamMap({ slug: 'alpha' }));
    await TestBed.configureTestingModule({
      imports: [MembershipsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: paramMap$.asObservable() } },
      ],
    }).compileComponents();
    mock = TestBed.inject(HttpTestingController);
  });

  async function flushInitial() {
    const project = mock.expectOne('/api/projects/by-slug/alpha');
    project.flush({
      data: {
        id: 'p1', name: 'Alpha', slug: 'alpha', projectType: 'feature-delivery',
        owningTeam: { id: 't1', name: 'Engineering' },
        inFlightWorkItems: 0, createdAt: '2026-05-01T00:00:00Z',
      },
    });
    // Let getProjectBySlug resolve before the Promise.all triplet fires.
    await Promise.resolve(); await Promise.resolve();

    const memberships = mock.expectOne('/api/projects/p1/memberships');
    memberships.flush({
      data: [{
        id: 'm1',
        member: { id: 'op', displayName: 'Operator', email: 'op@x' },
        roles: ['operator'],
        createdAt: '2026-05-01T00:00:00Z',
      }],
    });

    mock.expectOne('/api/roles').flush({ data: [{ id: 'r1', key: 'operator', name: 'Operator', isSystem: true }] });

    mock.expectOne(r => r.url === '/api/members').flush({
      data: [
        { id: 'op', displayName: 'Operator', email: 'op@x', status: 'Active', createdAt: '2026-05-01T00:00:00Z' },
        { id: 'alice', displayName: 'Alice', email: 'alice@x', status: 'Active', createdAt: '2026-05-01T00:00:00Z' },
      ],
      meta: { totalCount: 2, page: 1, pageSize: 200 },
    });
  }

  it('loads project + memberships + roles + members in parallel and renders the project caption', async () => {
    const fixture = TestBed.createComponent(MembershipsPage);
    fixture.detectChanges();
    await flushInitial();
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Memberships');
    expect(html.textContent).toContain('alpha');         // slug caption
    expect(html.textContent).toContain('Engineering');   // team owner
    expect(html.textContent).toContain('Operator');       // sole membership row
  });

  it('add happy-path POSTs and refreshes', async () => {
    const fixture = TestBed.createComponent(MembershipsPage);
    fixture.detectChanges();
    await flushInitial();
    await fixture.whenStable();
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      openAdd(): void;
      onModalSubmit(r: { memberId: string; roleKeys: string[] }): Promise<void>;
    };
    cmp.openAdd();
    fixture.detectChanges();
    void cmp.onModalSubmit({ memberId: 'alice', roleKeys: ['operator'] });
    await Promise.resolve();

    mock.expectOne(r => r.url === '/api/projects/p1/memberships' && r.method === 'POST').flush({
      data: {
        id: 'm2',
        member: { id: 'alice', displayName: 'Alice', email: 'alice@x' },
        roles: ['operator'],
        createdAt: '2026-05-01T00:00:00Z',
      },
    });
    // Several yield ticks: onModalSubmit's await POST → refreshMemberships → GET issued.
    for (let i = 0; i < 6; i++) await Promise.resolve();

    // Refresh after add.
    mock.expectOne('/api/projects/p1/memberships').flush({
      data: [
        { id: 'm1', member: { id: 'op', displayName: 'Operator', email: 'op@x' }, roles: ['operator'], createdAt: '2026-05-01T00:00:00Z' },
        { id: 'm2', member: { id: 'alice', displayName: 'Alice', email: 'alice@x' }, roles: ['operator'], createdAt: '2026-05-01T00:00:00Z' },
      ],
    });
    for (let i = 0; i < 4; i++) await Promise.resolve();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Alice');
  });
});
