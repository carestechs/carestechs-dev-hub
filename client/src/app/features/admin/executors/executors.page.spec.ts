import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ExecutorsPage } from './executors.page';

describe('ExecutorsPage', () => {
  let mock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExecutorsPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    mock = TestBed.inject(HttpTestingController);
  });

  function flushBootstrap(items: { id: string; key: string }[]) {
    mock.expectOne(r => r.url === '/api/admin/executors').flush({
      data: items.map(i => ({
        id: i.id, key: i.key, displayName: i.key, baseUrl: 'http://x', credentialsRef: 'EXEC_X',
        status: 'Active', checkpointContracts: [], createdAt: '2026-05-01T00:00:00Z',
      })),
      meta: { totalCount: items.length, page: 1, pageSize: 20 },
    });
    mock.expectOne(r => r.url === '/api/roles').flush({
      data: [{ id: 'r1', key: 'operator', name: 'Operator', isSystem: true }],
    });
  }

  it('renders rows on load and shows the Register button', async () => {
    const fixture = TestBed.createComponent(ExecutorsPage);
    fixture.detectChanges();
    flushBootstrap([{ id: 'e1', key: 'feature-delivery-v1' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('feature-delivery-v1');
    expect(html.textContent).toContain('+ Register executor');
  });

  it('register happy-path POSTs and refreshes', async () => {
    const fixture = TestBed.createComponent(ExecutorsPage);
    fixture.detectChanges();
    flushBootstrap([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      openRegister(): void;
      onModalSubmit(r: unknown): Promise<void>;
    };
    cmp.openRegister();
    fixture.detectChanges();
    void cmp.onModalSubmit({
      key: 'new-exec',
      displayName: 'New',
      baseUrl: 'http://localhost:9000',
      credentialsRef: 'NEW_TOKEN',
      checkpointContracts: [
        { checkpointKey: 'approve', displayName: 'Approve', requiredRoleKey: 'operator', allowedOutcomes: ['approve'] },
      ],
    });
    await Promise.resolve();

    mock.expectOne(r => r.url === '/api/admin/executors' && r.method === 'POST').flush({
      data: {
        id: 'e2', key: 'new-exec', displayName: 'New', baseUrl: 'http://localhost:9000',
        credentialsRef: 'NEW_TOKEN', status: 'Active',
        checkpointContracts: [{ checkpointKey: 'approve', displayName: 'Approve', requiredRoleKey: 'operator', allowedOutcomes: ['approve'] }],
        createdAt: '2026-05-01T00:00:00Z',
      },
    });
    for (let i = 0; i < 6; i++) await Promise.resolve();

    mock.expectOne(r => r.url === '/api/admin/executors').flush({
      data: [{
        id: 'e2', key: 'new-exec', displayName: 'New', baseUrl: 'http://localhost:9000',
        credentialsRef: 'NEW_TOKEN', status: 'Active', checkpointContracts: [], createdAt: '2026-05-01T00:00:00Z',
      }],
      meta: { totalCount: 1, page: 1, pageSize: 20 },
    });
    for (let i = 0; i < 4; i++) await Promise.resolve();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('new-exec');
  });

  it('surfaces a 409 from delete inside the confirm dialog', async () => {
    const fixture = TestBed.createComponent(ExecutorsPage);
    fixture.detectChanges();
    flushBootstrap([{ id: 'e1', key: 'feature-delivery-v1' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      askDelete(e: { id: string; key: string }): void;
      onDeleteConfirm(): Promise<void>;
    };
    cmp.askDelete({ id: 'e1', key: 'feature-delivery-v1' });
    fixture.detectChanges();
    void cmp.onDeleteConfirm();
    await Promise.resolve();

    const del = mock.expectOne(r => r.url === '/api/admin/executors/e1' && r.method === 'DELETE');
    del.flush(
      { type: '/probs/conflict', title: 'Conflict', status: 409, detail: 'Cannot delete an executor with active bindings.' },
      { status: 409, statusText: 'Conflict', headers: { 'content-type': 'application/problem+json' } },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Cannot delete an executor with active bindings.');
    expect(html.querySelector('[role=dialog]')).toBeTruthy();
  });
});
