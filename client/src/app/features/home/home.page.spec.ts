import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { HomePage } from './home.page';

describe('HomePage', () => {
  let auth: AuthService;
  let mock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HomePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    auth = TestBed.inject(AuthService);
    mock = TestBed.inject(HttpTestingController);
  });

  function setMember(name: string | null) {
    const slot = (auth as unknown as { _member: { set: (v: unknown) => void } })._member;
    slot.set(name === null ? null : { id: 'm1', displayName: name, email: 'op@devhub.local' });
  }

  function flushProjects(items: { id: string; name: string; slug: string }[] = []) {
    const req = mock.expectOne(r => r.url === '/api/projects');
    req.flush({
      data: items.map(i => ({
        id: i.id, name: i.name, slug: i.slug,
        projectType: 'feature-delivery',
        owningTeam: { id: 't', name: 'Eng' },
        inFlightWorkItems: 0,
        createdAt: '2026-05-01T00:00:00Z',
      })),
      meta: { totalCount: items.length, page: 1, pageSize: 3 },
    });
  }

  it('greets the current member by display name', async () => {
    setMember('Operator');
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    flushProjects([]);
    await fixture.whenStable();
    fixture.detectChanges();
    const h1 = fixture.nativeElement.querySelector('h1') as HTMLHeadingElement;
    expect(h1.textContent).toContain('Welcome back, Operator');
  });

  it('renders the pending-on-you and projects sections', async () => {
    setMember('Op');
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    flushProjects([]);
    await fixture.whenStable();
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("You're all caught up.");
    expect(text).toContain('No projects yet.');
  });

  it('renders the project grid when the list is non-empty', async () => {
    setMember('Op');
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    flushProjects([
      { id: 'a', name: 'Alpha', slug: 'alpha' },
      { id: 'b', name: 'Beta',  slug: 'beta' },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelectorAll('project-card')).toHaveSize(2);
    expect(html.textContent).toContain('Browse all');
  });

  it('falls back to a generic welcome when no member is set', async () => {
    setMember(null);
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    flushProjects([]);
    await fixture.whenStable();
    fixture.detectChanges();
    const h1 = fixture.nativeElement.querySelector('h1') as HTMLHeadingElement;
    expect(h1.textContent).toContain('Welcome');
    expect(h1.textContent).not.toContain('Welcome back,');
  });
});
