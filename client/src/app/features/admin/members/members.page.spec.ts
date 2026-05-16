import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MembersPage } from './members.page';

describe('MembersPage', () => {
  let mock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MembersPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    mock = TestBed.inject(HttpTestingController);
  });

  function flushMembers(items: { id: string; displayName: string; email: string; status?: string }[]) {
    const req = mock.expectOne(r => r.url === '/api/members');
    req.flush({
      data: items.map(i => ({
        id: i.id,
        displayName: i.displayName,
        email: i.email,
        status: i.status ?? 'Active',
        createdAt: '2026-05-01T00:00:00Z',
      })),
      meta: { totalCount: items.length, page: 1, pageSize: 20 },
    });
  }

  it('renders the list with status badges', async () => {
    const fixture = TestBed.createComponent(MembersPage);
    fixture.detectChanges();
    flushMembers([
      { id: 'a', displayName: 'Operator', email: 'op@x' },
      { id: 'b', displayName: 'Alice', email: 'alice@x', status: 'Invited' },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Operator');
    expect(html.textContent).toContain('alice@x');
    expect(html.textContent).toContain('Invited');
  });

  it('debounces the search and re-queries the API', fakeAsync(() => {
    const fixture = TestBed.createComponent(MembersPage);
    fixture.detectChanges();
    flushMembers([]);
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      search: { setValue(v: string): void };
    };
    cmp.search.setValue('alice');
    tick(300); // beyond 250ms debounce

    const req = mock.expectOne(r => r.url === '/api/members');
    expect(req.request.params.get('q')).toBe('alice');
    req.flush({ data: [], meta: { totalCount: 0, page: 1, pageSize: 20 } });
  }));

  it('invite happy-path closes the modal and reloads', async () => {
    const fixture = TestBed.createComponent(MembersPage);
    fixture.detectChanges();
    flushMembers([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      openInvite(): void;
      onModalSubmit(r: { displayName: string; email: string }): Promise<void>;
    };
    cmp.openInvite();
    fixture.detectChanges();
    void cmp.onModalSubmit({ displayName: 'Bob', email: 'bob@x' });
    await Promise.resolve();

    mock.expectOne(r => r.url === '/api/members' && r.method === 'POST').flush({
      data: { id: 'bob', displayName: 'Bob', email: 'bob@x', status: 'Invited', createdAt: '2026-05-01T00:00:00Z' },
    });
    await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); await Promise.resolve();
    flushMembers([{ id: 'bob', displayName: 'Bob', email: 'bob@x', status: 'Invited' }]);
    await Promise.resolve(); await Promise.resolve();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Bob');
    expect((fixture.nativeElement as HTMLElement).querySelector('[role=dialog]')).toBeNull();
  });
});
