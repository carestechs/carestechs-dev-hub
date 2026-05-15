import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let mock: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpClient);
    mock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => mock.verify());

  it('does not attach bearer to non /api/ requests', () => {
    auth.setAccessToken('jwt.a');
    http.get('/not-api/x').subscribe();
    const req = mock.expectOne('/not-api/x');
    expect(req.request.headers.get('Authorization')).toBeNull();
    req.flush({});
  });

  it('attaches Authorization: Bearer to /api/* when a token exists', () => {
    auth.setAccessToken('jwt.a');
    http.get('/api/foo').subscribe();
    const req = mock.expectOne('/api/foo');
    expect(req.request.headers.get('Authorization')).toBe('Bearer jwt.a');
    req.flush({});
  });

  it('on 401, refreshes once and replays with the new token', (done) => {
    auth.setAccessToken('jwt.old');
    let observed: unknown;
    http.get('/api/foo').subscribe({
      next: r => { observed = r; },
      complete: () => {
        expect(observed).toEqual({ ok: true });
        expect(auth.token()).toBe('jwt.new');
        done();
      },
    });

    const first = mock.expectOne('/api/foo');
    expect(first.request.headers.get('Authorization')).toBe('Bearer jwt.old');
    first.flush({}, { status: 401, statusText: 'Unauthorized' });

    const refresh = mock.expectOne('/api/auth/refresh');
    refresh.flush({ data: { accessToken: 'jwt.new', expiresAt: '2099-01-01T00:00:00Z' } });

    const replay = mock.expectOne('/api/foo');
    expect(replay.request.headers.get('Authorization')).toBe('Bearer jwt.new');
    replay.flush({ ok: true });
  });

  it('on second 401, clears state and routes to /login', (done) => {
    auth.setAccessToken('jwt.old');
    const router = TestBed.inject(Router);
    const navigateSpy = spyOn(router, 'navigateByUrl').and.returnValue(Promise.resolve(true));

    http.get('/api/foo').subscribe({
      next: () => fail('should error'),
      error: () => {
        expect(auth.isAuthenticated()).toBeFalse();
        expect(navigateSpy).toHaveBeenCalledWith('/login');
        done();
      },
    });

    mock.expectOne('/api/foo').flush({}, { status: 401, statusText: 'Unauthorized' });
    mock.expectOne('/api/auth/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });
  });

  it('does not retry refresh when the failing call is /api/auth/login', (done) => {
    http.post('/api/auth/login', {}).subscribe({
      next: () => fail('should error'),
      error: () => done(),
    });
    mock.expectOne('/api/auth/login').flush({}, { status: 401, statusText: 'Unauthorized' });
    // No follow-up refresh call.
    mock.expectNone('/api/auth/refresh');
  });
});
