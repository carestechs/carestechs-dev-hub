import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ProjectListPage } from './project-list.page';

describe('ProjectListPage', () => {
  let mock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProjectListPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    mock = TestBed.inject(HttpTestingController);
  });

  function render() {
    const fixture = TestBed.createComponent(ProjectListPage);
    fixture.detectChanges(); // triggers constructor's load()
    return fixture;
  }

  function flushProjects(items: { id: string; name: string; slug: string }[]) {
    const req = mock.expectOne(r => r.url === '/api/projects');
    req.flush({
      data: items.map(i => ({
        id: i.id, name: i.name, slug: i.slug,
        projectType: 'feature-delivery',
        owningTeam: { id: 't', name: 'Eng' },
        inFlightWorkItems: 0,
        createdAt: '2026-05-01T00:00:00Z',
      })),
      meta: { totalCount: items.length, page: 1, pageSize: 20 },
    });
  }

  function flushTeams() {
    const req = mock.expectOne(r => r.url === '/api/teams');
    req.flush({ data: [], meta: { totalCount: 0, page: 1, pageSize: 100 } });
  }

  it('renders the heading and a grid of project cards after load', async () => {
    const fixture = render();
    flushProjects([
      { id: 'a', name: 'Alpha', slug: 'alpha' },
      { id: 'b', name: 'Beta',  slug: 'beta' },
    ]);
    flushTeams();
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('h1')?.textContent).toContain('Projects');
    expect(html.querySelectorAll('project-card')).toHaveSize(2);
    expect(html.textContent).toContain('Alpha');
  });

  it('renders the empty state when the list resolves to []', async () => {
    const fixture = render();
    flushProjects([]);
    flushTeams();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No projects yet.');
  });

  it('shows skeleton tiles while loading', () => {
    const fixture = render();
    // Don't flush yet — still loading.
    expect((fixture.nativeElement as HTMLElement).querySelector('[aria-busy="true"]')).toBeTruthy();
    flushProjects([]);
    flushTeams();
  });

  it('navigates to /projects/:slug on card open', async () => {
    const fixture = render();
    flushProjects([{ id: 'a', name: 'Alpha', slug: 'alpha' }]);
    flushTeams();
    await fixture.whenStable();
    fixture.detectChanges();

    const router = TestBed.inject(Router);
    const navSpy = spyOn(router, 'navigate').and.resolveTo(true);

    const card = (fixture.nativeElement as HTMLElement).querySelector('project-card') as HTMLElement;
    card.click();
    expect(navSpy).toHaveBeenCalledWith(['/projects', 'alpha']);
  });

  it('debounces filter changes and re-queries the API', fakeAsync(() => {
    const fixture = render();
    flushProjects([]);
    flushTeams();
    fixture.detectChanges();

    // Tweak the search field.
    const cmp = fixture.componentInstance as unknown as {
      filterForm: { controls: { q: { setValue: (v: string) => void } } };
    };
    cmp.filterForm.controls.q.setValue('alpha');
    tick(300); // > 250ms debounce

    const req = mock.expectOne(r => r.url === '/api/projects');
    req.flush({ data: [], meta: { totalCount: 0, page: 1, pageSize: 20 } });
  }));
});
