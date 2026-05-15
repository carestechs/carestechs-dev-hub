import { HttpClient, HttpErrorResponse, HttpHeaders, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import type { AppError } from './app-error';
import { problemDetailsInterceptor } from './problem-details.interceptor';

describe('problemDetailsInterceptor', () => {
  let http: HttpClient;
  let mock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    mock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => mock.verify());

  it('parses application/problem+json bodies into a typed AppError', (done) => {
    http.get('/api/foo').subscribe({
      next: () => fail('should error'),
      error: (err: HttpErrorResponse) => {
        const e = err.error as AppError;
        expect(e.type).toBe('/probs/unauthorized');
        expect(e.title).toBe('Unauthorized');
        expect(e.status).toBe(401);
        expect(e.detail).toBe('Invalid email or password.');
        expect(e.correlationId).toBe('00-abc-123');
        expect(e.errors).toEqual({ password: ['Required.'] });
        done();
      },
    });
    mock.expectOne('/api/foo').flush(
      {
        type: '/probs/unauthorized',
        title: 'Unauthorized',
        status: 401,
        detail: 'Invalid email or password.',
        correlationId: '00-abc-123',
        errors: { password: ['Required.'] },
      },
      {
        status: 401,
        statusText: 'Unauthorized',
        headers: new HttpHeaders({ 'content-type': 'application/problem+json' }),
      },
    );
  });

  it('falls back to a synthetic AppError for non-problem-details errors', (done) => {
    http.get('/api/foo').subscribe({
      next: () => fail('should error'),
      error: (err: HttpErrorResponse) => {
        const e = err.error as AppError;
        expect(e.type).toBe('about:blank');
        expect(e.title).toContain('500');
        expect(e.status).toBe(500);
        done();
      },
    });
    mock.expectOne('/api/foo').flush('oh no', { status: 500, statusText: 'Server Error' });
  });

  it('uses "Network error" title for status 0', (done) => {
    http.get('/api/foo').subscribe({
      next: () => fail('should error'),
      error: (err: HttpErrorResponse) => {
        const e = err.error as AppError;
        expect(e.title).toBe('Network error');
        done();
      },
    });
    mock.expectOne('/api/foo').error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown' });
  });
});
