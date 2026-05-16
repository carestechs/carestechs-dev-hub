import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ExecutorBindingsPage } from './executor-bindings.page';

describe('ExecutorBindingsPage', () => {
  let mock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExecutorBindingsPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    mock = TestBed.inject(HttpTestingController);
  });

  function flushBootstrap(bindings: { id: string; projectType: string }[], execs: { id: string; key: string }[]) {
    mock.expectOne(r => r.url === '/api/admin/executor-bindings').flush({
      data: bindings.map(b => ({
        id: b.id, projectType: b.projectType, executorId: 'e1', executorKey: 'feature-delivery-v1',
        executorDisplayName: 'Feature Delivery v1', executorStatus: 'Active', createdAt: '2026-05-01T00:00:00Z',
      })),
      meta: { totalCount: bindings.length, page: 1, pageSize: 20 },
    });
    mock.expectOne(r => r.url === '/api/admin/executors').flush({
      data: execs.map(e => ({
        id: e.id, key: e.key, displayName: e.key, baseUrl: 'http://x', credentialsRef: 'EXEC_X',
        status: 'Active', checkpointContracts: [], createdAt: '2026-05-01T00:00:00Z',
      })),
      meta: { totalCount: execs.length, page: 1, pageSize: 200 },
    });
  }

  it('renders rows on load and shows the New binding button', async () => {
    const fixture = TestBed.createComponent(ExecutorBindingsPage);
    fixture.detectChanges();
    flushBootstrap(
      [{ id: 'b1', projectType: 'feature-delivery' }],
      [{ id: 'e1', key: 'feature-delivery-v1' }],
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('feature-delivery');
    expect(html.textContent).toContain('+ New binding');
  });

  it('create happy-path POSTs and refreshes', async () => {
    const fixture = TestBed.createComponent(ExecutorBindingsPage);
    fixture.detectChanges();
    flushBootstrap([], [{ id: 'e1', key: 'feature-delivery-v1' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      openNew(): void;
      onModalSubmit(r: { projectType: string; executorId: string }): Promise<void>;
    };
    cmp.openNew();
    fixture.detectChanges();
    void cmp.onModalSubmit({ projectType: 'feature-delivery', executorId: 'e1' });
    await Promise.resolve();

    mock.expectOne(r => r.url === '/api/admin/executor-bindings' && r.method === 'POST').flush({
      data: {
        id: 'b1', projectType: 'feature-delivery', executorId: 'e1', executorKey: 'feature-delivery-v1',
        executorDisplayName: 'Feature Delivery v1', executorStatus: 'Active', createdAt: '2026-05-01T00:00:00Z',
      },
    });
    for (let i = 0; i < 6; i++) await Promise.resolve();

    mock.expectOne(r => r.url === '/api/admin/executor-bindings').flush({
      data: [{
        id: 'b1', projectType: 'feature-delivery', executorId: 'e1', executorKey: 'feature-delivery-v1',
        executorDisplayName: 'Feature Delivery v1', executorStatus: 'Active', createdAt: '2026-05-01T00:00:00Z',
      }],
      meta: { totalCount: 1, page: 1, pageSize: 20 },
    });
    for (let i = 0; i < 4; i++) await Promise.resolve();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('feature-delivery');
  });

  it('surfaces a 409 from create inside the modal', async () => {
    const fixture = TestBed.createComponent(ExecutorBindingsPage);
    fixture.detectChanges();
    flushBootstrap([], [{ id: 'e1', key: 'feature-delivery-v1' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      openNew(): void;
      onModalSubmit(r: { projectType: string; executorId: string }): Promise<void>;
    };
    cmp.openNew();
    fixture.detectChanges();
    void cmp.onModalSubmit({ projectType: 'feature-delivery', executorId: 'e1' });
    await Promise.resolve();

    const post = mock.expectOne(r => r.url === '/api/admin/executor-bindings' && r.method === 'POST');
    post.flush(
      { type: '/probs/conflict', title: 'Conflict', status: 409,
        detail: "Project type 'feature-delivery' is already bound to an executor." },
      { status: 409, statusText: 'Conflict', headers: { 'content-type': 'application/problem+json' } },
    );
    for (let i = 0; i < 4; i++) await Promise.resolve();
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('already bound');
    expect(html.querySelector('[role=dialog]')).toBeTruthy();
  });
});
