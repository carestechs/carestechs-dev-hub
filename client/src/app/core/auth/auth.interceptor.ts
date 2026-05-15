import { HttpErrorResponse, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, Observable, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const SKIP_PATHS = new Set<string>([
  '/api/auth/login',
  '/api/auth/refresh',
]);

/**
 * Attaches Bearer to /api/* requests. On 401 from a non-skip endpoint, attempts a
 * single silent refresh and replays the request once. Second 401 → clear state +
 * route to /login. The refresh call itself is coalesced inside AuthService.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!req.url.startsWith('/api/')) return next(req);

  const tokenReq = withBearer(req, auth.token());

  return next(tokenReq).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse) || err.status !== 401) {
        return throwError(() => err);
      }
      if (SKIP_PATHS.has(stripQuery(req.url))) {
        // 401 on the login/refresh endpoints themselves means bad credentials /
        // expired refresh — surface as-is, do NOT recurse into another refresh.
        return throwError(() => err);
      }
      return auth.ensureFreshAccessToken().pipe(
        switchMap(newToken => {
          if (!newToken) {
            auth.clear();
            void router.navigateByUrl('/login');
            return throwError(() => err);
          }
          return next(withBearer(req, newToken));
        }),
      );
    }),
  );
};

function withBearer(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  if (!token) return req;
  return req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

function stripQuery(url: string): string {
  const qIndex = url.indexOf('?');
  return qIndex === -1 ? url : url.substring(0, qIndex);
}
