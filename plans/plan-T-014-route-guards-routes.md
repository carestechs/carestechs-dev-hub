# Implementation Plan: T-014 — Route guards and route configuration

## Task Reference
- **Task ID:** T-014
- **Type:** Frontend
- **Workflow:** standard
- **Complexity:** S
- **Rationale:** Defense in depth in the UI; the server is the authoritative gate. Required for AC-2 / AC-3 navigation behavior.

## Overview
Two functional guards (`authGuard`, `anonGuard`) and the top-level routes table that wires login → public layout and everything else → app shell, with lazy-loaded feature pages.

## Implementation Steps

### Step 1: Guards
**File:** `client/src/app/core/auth/auth.guard.ts`
**Action:** Create
```ts
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isAuthenticated()) return true;
  return router.parseUrl('/login');
};

export const anonGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) return true;
  return router.parseUrl('/');
};
```

### Step 2: Routes
**File:** `client/src/app/app.routes.ts`
**Action:** Modify
```ts
export const routes: Routes = [
  {
    path: 'login',
    canActivate: [anonGuard],
    loadComponent: () => import('./core/layouts/public-layout/public-layout.component').then(m => m.PublicLayoutComponent),
    children: [
      { path: '', loadComponent: () => import('./features/login/login.page').then(m => m.LoginPage) }
    ],
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./core/layouts/app-shell/app-shell.component').then(m => m.AppShellComponent),
    children: [
      { path: '', pathMatch: 'full', loadComponent: () => import('./features/home/home.page').then(m => m.HomePage) },
      { path: 'me', loadComponent: () => import('./features/profile/profile.page').then(m => m.ProfilePage) }, // placeholder; full page later
    ],
  },
  { path: '**', redirectTo: '' },
];
```
A minimal `ProfilePage` stub component lives in `features/profile/` with just a "Sign out" button calling `auth.logout()` and navigating to `/login`.

### Step 3: Specs
**File:** `client/src/app/core/auth/auth.guard.spec.ts`
**Action:** Create
- `authGuard` returns `true` when authenticated; returns a `UrlTree('/login')` when not.
- `anonGuard` returns `true` when not authenticated; returns a `UrlTree('/')` when authenticated.
Mock `AuthService` via `TestBed.overrideProvider`.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `client/src/app/core/auth/auth.guard.ts` | Create | Functional guards |
| `client/src/app/core/auth/auth.guard.spec.ts` | Create | Guard tests |
| `client/src/app/app.routes.ts` | Modify | Top-level routes wired to layouts |
| `client/src/app/features/profile/profile.page.{ts,html}` | Create | Minimal stub with logout |

## Edge Cases & Risks
- **Race with `provideAppInitializer`** — guards only run *after* the initializer has resolved (Angular waits on initializers before bootstrapping routes), so `isAuthenticated()` is deterministic when guards execute. Document this dependency in `T-013`.
- **`**` redirect to `/` when unauthenticated** — the `authGuard` on `/` will then redirect to `/login`. Two hops on a deep-link miss; acceptable.
- **Lazy chunk loading on slow networks** — preloading is off by default; consider `withPreloading(PreloadAllModules)` later if it becomes noticeable.

## Acceptance Verification
- [ ] Navigating to `/` while unauthenticated lands on `/login`.
- [ ] Navigating to `/login` while authenticated lands on `/`.
- [ ] Routes lazy-load: only the relevant chunk loads on first visit (network panel shows `home.page-*.js` only after login).
- [ ] Guard specs pass under `ng test`.
