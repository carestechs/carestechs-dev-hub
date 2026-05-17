import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WorkItemsService } from './work-items.service';

describe('WorkItemsService', () => {
  let svc: WorkItemsService;
  let mock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(WorkItemsService);
    mock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => mock.verify());

  function workItemFixture(overrides: Record<string, unknown> = {}) {
    return {
      id: 'w1', projectId: 'p1', title: 'X',
      currentStatus: 'Running', currentCheckpointKey: null,
      executor: { id: 'e1', key: 'ex', displayName: 'Ex' },
      executorCorrelationMarker: 'abc',
      createdAt: '2026-05-01T00:00:00Z',
      createdBy: { id: 'm1', displayName: 'M' },
      executorState: {},
      workBranch: null,
      ...overrides,
    };
  }

  it('start sends POST with workBranch when provided (FEAT-008)', async () => {
    const p = svc.start('p1', { title: 'Demo', input: {}, workBranch: 'feat/abc' });
    const req = mock.expectOne('/api/projects/p1/work-items');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ title: 'Demo', input: {}, workBranch: 'feat/abc' });
    req.flush({ data: workItemFixture({ workBranch: 'feat/abc' }) });
    expect((await p).workBranch).toBe('feat/abc');
  });

  it('update sends PATCH with workBranch and unwraps the envelope', async () => {
    const p = svc.update('p1', 'w1', { workBranch: 'feat/new' });
    const req = mock.expectOne('/api/projects/p1/work-items/w1');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ workBranch: 'feat/new' });
    req.flush({ data: workItemFixture({ workBranch: 'feat/new' }) });
    expect((await p).workBranch).toBe('feat/new');
  });

  it('update sends PATCH with empty-string workBranch to clear the override', async () => {
    const p = svc.update('p1', 'w1', { workBranch: '' });
    const req = mock.expectOne('/api/projects/p1/work-items/w1');
    expect(req.request.body).toEqual({ workBranch: '' });
    req.flush({ data: workItemFixture({ workBranch: null }) });
    expect((await p).workBranch).toBeNull();
  });
});
