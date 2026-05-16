import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Operator-only routes. Unauthenticated → /login; authenticated non-operators → /.
 * Defense-in-depth only: the server is the authoritative gate on every admin action.
 */
export const operatorGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) return router.parseUrl('/login');
  if (!auth.isOperator()) return router.parseUrl('/');
  return true;
};
