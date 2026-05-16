import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentInjector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree, type ActivatedRouteSnapshot, type RouterStateSnapshot } from '@angular/router';
import { AuthService } from './auth.service';
import { operatorGuard } from './operator.guard';

describe('operatorGuard', () => {
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
  const state = { url: '/admin/teams' } as RouterStateSnapshot;

  function run() {
    return runInInjectionContext(env, () => operatorGuard(route, state));
  }

  it('redirects anonymous to /login', () => {
    const result = run();
    const router = TestBed.inject(Router);
    expect(result).toEqual(router.parseUrl('/login'));
    expect(result).toBeInstanceOf(UrlTree);
  });

  it('redirects non-operator authenticated users to /', () => {
    auth.setAccessToken('jwt'); // authenticated but no operator membership
    const result = run();
    const router = TestBed.inject(Router);
    expect(result).toEqual(router.parseUrl('/'));
  });

  it('allows operators through', () => {
    auth.setAccessToken('jwt');
    // Simulate isOperator by injecting an operator membership.
    const slot = (auth as unknown as { _memberships: { set: (v: unknown) => void } })._memberships;
    slot.set([{ projectId: 'p', projectSlug: 'p', roles: ['operator'] }]);
    expect(run()).toBeTrue();
  });
});
