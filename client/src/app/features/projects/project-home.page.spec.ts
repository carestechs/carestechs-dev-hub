import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
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

  it('renders the header after a successful load', async () => {
    const fixture = TestBed.createComponent(ProjectHomePage);
    fixture.detectChanges();

    const req = mock.expectOne('/api/projects/by-slug/alpha');
    req.flush({
      data: {
        id: 'a', name: 'Alpha', slug: 'alpha', projectType: 'feature-delivery',
        owningTeam: { id: 't', name: 'Engineering' },
        inFlightWorkItems: 2, createdAt: '2026-05-01T00:00:00Z',
      },
    });
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('h1')?.textContent).toContain('Alpha');
    expect(html.textContent).toContain('Engineering');
    expect(html.textContent).toContain('feature-delivery');
    expect(html.textContent).toContain('Work items land in FEAT-004.');
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

    mock.expectOne('/api/projects/by-slug/alpha').flush({
      data: {
        id: 'a', name: 'Alpha', slug: 'alpha', projectType: 't',
        owningTeam: { id: 't', name: 'Eng' }, inFlightWorkItems: 0, createdAt: '2026-05-01T00:00:00Z',
      },
    });
    await fixture.whenStable();

    paramMap$.next(convertToParamMap({ slug: 'beta' }));
    fixture.detectChanges();
    mock.expectOne('/api/projects/by-slug/beta').flush({
      data: {
        id: 'b', name: 'Beta', slug: 'beta', projectType: 't',
        owningTeam: { id: 't', name: 'Eng' }, inFlightWorkItems: 0, createdAt: '2026-05-01T00:00:00Z',
      },
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Beta');
  });
});
