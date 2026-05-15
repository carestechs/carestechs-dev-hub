import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import type { AppError } from './app-error';

/**
 * Normalizes server errors into a typed {@link AppError} carried on
 * <code>HttpErrorResponse.error</code>. Components read the typed shape; raw
 * HttpErrorResponse stays available for status-code checks.
 */
export const problemDetailsInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse)) return throwError(() => err);

      const contentType = err.headers?.get?.('content-type') ?? '';
      const isProblem = contentType.toLowerCase().startsWith('application/problem+json');

      const appError: AppError = isProblem && typeof err.error === 'object' && err.error !== null
        ? toAppError(err.error as Record<string, unknown>, err.status)
        : {
            type: 'about:blank',
            title: networkTitleFor(err),
            status: err.status,
            detail: err.message,
          };

      return throwError(() => new HttpErrorResponse({
        error: appError,
        headers: err.headers,
        status: err.status,
        statusText: err.statusText,
        url: err.url ?? undefined,
      }));
    }),
  );

function toAppError(body: Record<string, unknown>, fallbackStatus: number): AppError {
  return {
    type: typeof body['type'] === 'string' ? body['type'] : 'about:blank',
    title: typeof body['title'] === 'string' ? body['title'] : 'Request failed',
    status: typeof body['status'] === 'number' ? body['status'] : fallbackStatus,
    detail: typeof body['detail'] === 'string' ? body['detail'] : undefined,
    instance: typeof body['instance'] === 'string' ? body['instance'] : undefined,
    correlationId: typeof body['correlationId'] === 'string' ? body['correlationId'] : undefined,
    errors: isErrorsMap(body['errors']) ? body['errors'] : undefined,
  };
}

function isErrorsMap(v: unknown): v is Record<string, string[]> {
  return !!v && typeof v === 'object' && Object.values(v as Record<string, unknown>).every(
    arr => Array.isArray(arr) && arr.every(s => typeof s === 'string'),
  );
}

function networkTitleFor(err: HttpErrorResponse): string {
  if (err.status === 0) return 'Network error';
  return `Request failed (${err.status})`;
}
