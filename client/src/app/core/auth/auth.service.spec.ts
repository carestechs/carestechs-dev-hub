import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let svc: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts unauthenticated', () => {
    expect(svc.isAuthenticated()).toBeFalse();
    expect(svc.token()).toBeNull();
    expect(svc.currentMember()).toBeNull();
  });

  it('login stores token + member and resolves memberships', async () => {
    const p = svc.login('op@devhub.local', 'ChangeMe123!');

    const loginReq = http.expectOne('/api/auth/login');
    expect(loginReq.request.method).toBe('POST');
    expect(loginReq.request.withCredentials).toBeTrue();
    loginReq.flush({
      data: {
        accessToken: 'jwt.access',
        expiresAt: '2099-01-01T00:00:00Z',
        member: { id: 'm1', displayName: 'Operator', email: 'op@devhub.local' },
      },
    });

    // Yield so refreshMeQuiet() (chained after firstValueFrom resolves) can issue /me.
    await Promise.resolve(); await Promise.resolve();

    const meReq = http.expectOne('/api/auth/me');
    meReq.flush({
      data: {
        member: { id: 'm1', displayName: 'Operator', email: 'op@devhub.local' },
        memberships: [{ projectId: 'p1', projectSlug: 'proj-a', roles: ['operator'] }],
      },
    });

    await p;
    expect(svc.isAuthenticated()).toBeTrue();
    expect(svc.token()).toBe('jwt.access');
    expect(svc.currentMember()?.email).toBe('op@devhub.local');
    expect(svc.isOperator()).toBeTrue();
  });

  it('restore() succeeds on a valid refresh cookie', async () => {
    const p = svc.restore();
    http.expectOne('/api/auth/refresh').flush({
      data: { accessToken: 'jwt.fresh', expiresAt: '2099-01-01T00:00:00Z' },
    });
    await Promise.resolve(); await Promise.resolve();
    http.expectOne('/api/auth/me').flush({
      data: {
        member: { id: 'm1', displayName: 'Op', email: 'op@devhub.local' },
        memberships: [],
      },
    });
    await p;
    expect(svc.token()).toBe('jwt.fresh');
  });

  it('restore() clears state when refresh fails', async () => {
    svc.setAccessToken('stale');
    const p = svc.restore();
    http.expectOne('/api/auth/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });
    await p;
    expect(svc.isAuthenticated()).toBeFalse();
    expect(svc.token()).toBeNull();
  });

  it('logout posts to /api/auth/logout and clears state', async () => {
    svc.setAccessToken('jwt.access');
    const p = svc.logout();
    http.expectOne('/api/auth/logout').flush(null);
    await p;
    expect(svc.isAuthenticated()).toBeFalse();
  });

  it('ensureFreshAccessToken coalesces concurrent callers into one request', (done) => {
    const results: (string | null)[] = [];
    const want = 5;
    let resolved = 0;

    for (let i = 0; i < want; i++) {
      svc.ensureFreshAccessToken().subscribe(t => {
        results.push(t);
        if (++resolved === want) {
          expect(results.every(r => r === 'jwt.rotated')).toBeTrue();
          done();
        }
      });
    }

    // Exactly one network call regardless of N callers.
    const reqs = http.match('/api/auth/refresh');
    expect(reqs.length).toBe(1);
    reqs[0].flush({ data: { accessToken: 'jwt.rotated', expiresAt: '2099-01-01T00:00:00Z' } });
  });
});
