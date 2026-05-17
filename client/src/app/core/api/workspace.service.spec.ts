import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WorkspaceService } from './workspace.service';

describe('WorkspaceService', () => {
  let svc: WorkspaceService;
  let mock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(WorkspaceService);
    mock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => mock.verify());

  it('listTeams maps page params and returns the PagedEnvelope', async () => {
    const p = svc.listTeams({ page: 2, pageSize: 5, sortBy: 'name', sortDir: 'asc' });
    const req = mock.expectOne(r => r.url === '/api/teams');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('5');
    expect(req.request.params.get('sortBy')).toBe('name');
    expect(req.request.params.get('sortDir')).toBe('asc');
    req.flush({
      data: [{ id: 'a', name: 'Alpha', projectCount: 0, createdAt: '2026-05-01T00:00:00Z' }],
      meta: { totalCount: 1, page: 2, pageSize: 5, sortBy: 'name', sortDir: 'asc' },
    });
    const env = await p;
    expect(env.data).toHaveSize(1);
    expect(env.meta.totalCount).toBe(1);
  });

  it('createTeam unwraps the envelope', async () => {
    const p = svc.createTeam({ name: 'Engineering' });
    const req = mock.expectOne('/api/teams');
    expect(req.request.method).toBe('POST');
    req.flush({ data: { id: 'a', name: 'Engineering', projectCount: 0, createdAt: '2026-05-01T00:00:00Z' } });
    const team = await p;
    expect(team.name).toBe('Engineering');
  });

  it('updateTeam sends PATCH', async () => {
    const p = svc.updateTeam('123', { description: 'new' });
    const req = mock.expectOne('/api/teams/123');
    expect(req.request.method).toBe('PATCH');
    req.flush({ data: { id: '123', name: 'X', description: 'new', projectCount: 0, createdAt: '2026-05-01T00:00:00Z' } });
    expect((await p).description).toBe('new');
  });

  it('deleteTeam sends DELETE and resolves to void', async () => {
    const p = svc.deleteTeam('123');
    const req = mock.expectOne('/api/teams/123');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    await p;
  });

  it('listProjects supports filter params', async () => {
    const p = svc.listProjects({ teamId: 't1', projectType: 'feature-delivery' });
    const req = mock.expectOne(r => r.url === '/api/projects');
    expect(req.request.params.get('teamId')).toBe('t1');
    expect(req.request.params.get('projectType')).toBe('feature-delivery');
    req.flush({ data: [], meta: { totalCount: 0, page: 1, pageSize: 20 } });
    await p;
  });

  it('getProjectBySlug encodes the slug', async () => {
    const p = svc.getProjectBySlug('add csv export');
    const req = mock.expectOne(r => r.url === '/api/projects/by-slug/add%20csv%20export');
    expect(req.request.method).toBe('GET');
    req.flush({ data: {
      id: 'p1', name: 'X', slug: 'add csv export', projectType: 'f',
      owningTeam: { id: 't', name: 'T' }, inFlightWorkItems: 0, createdAt: '2026-05-01T00:00:00Z',
    } });
    expect((await p).slug).toBe('add csv export');
  });

  it('updateProject sends PATCH with repo + defaultBranch in the body (FEAT-008)', async () => {
    const p = svc.updateProject('p1', { repo: 'acme/widgets', defaultBranch: 'main' });
    const req = mock.expectOne('/api/projects/p1');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ repo: 'acme/widgets', defaultBranch: 'main' });
    req.flush({ data: {
      id: 'p1', name: 'X', slug: 'x', projectType: 'f',
      owningTeam: { id: 't', name: 'T' },
      repo: 'acme/widgets', defaultBranch: 'main',
      inFlightWorkItems: 0, createdAt: '2026-05-01T00:00:00Z',
    } });
    const dto = await p;
    expect(dto.repo).toBe('acme/widgets');
    expect(dto.defaultBranch).toBe('main');
  });

  it('addMembership wraps the body and unwraps the response', async () => {
    const p = svc.addMembership('p1', { memberId: 'm1', roleKeys: ['operator'] });
    const req = mock.expectOne('/api/projects/p1/memberships');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ memberId: 'm1', roleKeys: ['operator'] });
    req.flush({ data: {
      id: 'pm1', member: { id: 'm1', displayName: 'A', email: 'a@x' },
      roles: ['operator'], createdAt: '2026-05-01T00:00:00Z',
    } });
    expect((await p).id).toBe('pm1');
  });
});
