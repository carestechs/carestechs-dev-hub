import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TeamsPage } from './teams.page';

describe('TeamsPage', () => {
  let mock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TeamsPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    mock = TestBed.inject(HttpTestingController);
  });

  function flushTeams(items: { id: string; name: string }[]) {
    const req = mock.expectOne(r => r.url === '/api/teams');
    req.flush({
      data: items.map(i => ({ id: i.id, name: i.name, description: null, projectCount: 0, createdAt: '2026-05-01T00:00:00Z' })),
      meta: { totalCount: items.length, page: 1, pageSize: 20, sortBy: 'name', sortDir: 'asc' },
    });
  }

  it('renders rows on load and shows the New team button', async () => {
    const fixture = TestBed.createComponent(TeamsPage);
    fixture.detectChanges();
    flushTeams([{ id: 'a', name: 'Engineering' }, { id: 'b', name: 'Design' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Engineering');
    expect(html.textContent).toContain('Design');
    expect(html.textContent).toContain('+ New team');
  });

  it('opens the modal, submits a create, and refreshes the list', async () => {
    const fixture = TestBed.createComponent(TeamsPage);
    fixture.detectChanges();
    flushTeams([]);
    await fixture.whenStable();
    fixture.detectChanges();

    // The empty state has its own "+ New team" button — click it.
    const create = (fixture.nativeElement as HTMLElement)
      .querySelectorAll('button');
    const newBtn = Array.from(create).find(b => b.textContent?.includes('New team')) as HTMLButtonElement;
    newBtn.click();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role=dialog]')).toBeTruthy();

    // Simulate the modal submitting a payload.
    const cmp = fixture.componentInstance as unknown as { onModalSubmit(r: { name: string; description?: string }): Promise<void> };
    void cmp.onModalSubmit({ name: 'Platform' });
    await Promise.resolve(); // let the POST be queued by the await firstValueFrom(...)

    // POST then list reload.
    const post = mock.expectOne(r => r.url === '/api/teams' && r.method === 'POST');
    post.flush({ data: { id: 'p', name: 'Platform', description: null, projectCount: 0, createdAt: '2026-05-01T00:00:00Z' } });
    // Let postUnwrap's await + modalOpen.set + this.load() all queue up.
    await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); await Promise.resolve();
    flushTeams([{ id: 'p', name: 'Platform' }]);
    await Promise.resolve(); await Promise.resolve();
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Platform');
    expect(html.querySelector('[role=dialog]')).toBeNull();
  });

  it('surfaces a 409 from delete inside the confirm dialog', async () => {
    const fixture = TestBed.createComponent(TeamsPage);
    fixture.detectChanges();
    flushTeams([{ id: 'a', name: 'Engineering' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      askDelete(t: { id: string; name: string }): void;
      onDeleteConfirm(): Promise<void>;
    };
    cmp.askDelete({ id: 'a', name: 'Engineering' });
    fixture.detectChanges();
    void cmp.onDeleteConfirm();
    await Promise.resolve();

    const del = mock.expectOne(r => r.url === '/api/teams/a' && r.method === 'DELETE');
    del.flush(
      { type: '/probs/conflict', title: 'Conflict', status: 409, detail: 'Cannot delete a team that owns projects.' },
      { status: 409, statusText: 'Conflict', headers: { 'content-type': 'application/problem+json' } },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Cannot delete a team that owns projects.');
    expect(html.querySelector('[role=dialog]')).toBeTruthy(); // dialog stays open on error
  });
});
