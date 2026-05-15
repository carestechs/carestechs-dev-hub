import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentInjector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree, type ActivatedRouteSnapshot, type RouterStateSnapshot } from '@angular/router';
import { anonGuard, authGuard } from './auth.guard';
import { AuthService } from './auth.service';

describe('auth.guard', () => {
  let auth: AuthService;
  let env: EnvironmentInjector;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    auth = TestBed.inject(AuthService);
    env = TestBed.inject(EnvironmentInjector);
  });

  const route = {} as ActivatedRouteSnapshot;
  const state = { url: '/' } as RouterStateSnapshot;

  function run<T>(fn: () => T): T {
    return runInInjectionContext(env, fn);
  }

  describe('authGuard', () => {
    it('allows authenticated callers', () => {
      auth.setAccessToken('jwt');
      const result = run(() => authGuard(route, state));
      expect(result).toBe(true);
    });

    it('redirects unauthenticated to /login', () => {
      const result = run(() => authGuard(route, state));
      const router = TestBed.inject(Router);
      expect(result).toEqual(router.parseUrl('/login'));
    });
  });

  describe('anonGuard', () => {
    it('allows unauthenticated callers', () => {
      const result = run(() => anonGuard(route, state));
      expect(result).toBe(true);
    });

    it('redirects authenticated callers to /', () => {
      auth.setAccessToken('jwt');
      const result = run(() => anonGuard(route, state));
      const router = TestBed.inject(Router);
      const expected = router.parseUrl('/');
      expect(result).toEqual(expected);
      expect(result).toBeInstanceOf(UrlTree);
    });
  });
});
