# Implementation Plan: T-013 — Auth core: token storage, HTTP interceptor, problem-details normalizer

## Task Reference
- **Task ID:** T-013
- **Type:** Frontend
- **Workflow:** standard
- **Complexity:** L
- **Rationale:** FEAT-001 AC-3 requires login → token attach → empty home. Centralized interceptors avoid per-component error handling.

## Overview
Three pieces working together:
1. `AuthService` — in-memory access token and current member signals; `login()`, `logout()`, `restore()` API.
2. `authInterceptor` — attaches `Authorization: Bearer`, handles 401 with one silent refresh + replay.
3. `problemDetailsInterceptor` — translates `application/problem+json` responses into a typed `AppError`.

## Implementation Steps

### Step 1: Types
**Files:** `client/src/app/core/errors/app-error.ts`, `client/src/app/core/auth/auth.types.ts`
**Action:** Create
```ts
// app-error.ts
export interface AppError {
  type: string;          // RFC 7807 type URI
  title: string;
  status: number;
  detail?: string;
  instance?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
}

// auth.types.ts
export interface Member { id: string; displayName: string; email: string; }
export interface Membership { projectId: string; projectSlug: string; roles: string[]; }
export interface LoginResponse { accessToken: string; expiresAt: string; member: Member; }
export interface MeResponse { member: Member; memberships: Membership[]; }
```

### Step 2: AuthService
**File:** `client/src/app/core/auth/auth.service.ts`
**Action:** Create
```ts
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly _token = signal<string | null>(null);
  private readonly _member = signal<Member | null>(null);
  private readonly _memberships = signal<Membership[]>([]);

  readonly token = this._token.asReadonly();
  readonly currentMember = this._member.asReadonly();
  readonly memberships = this._memberships.asReadonly();
  readonly isAuthenticated = computed(() => this._token() !== null);
  readonly isOperator = computed(() => this._memberships().some(m => m.roles.includes('operator')) ||
                                       // fallback: token claim — Identity adds `role: operator` for the seed
                                       false);

  async login(email: string, password: string): Promise<void> {
    const env = await firstValueFrom(this.http.post<{ data: LoginResponse }>('/api/auth/login', { email, password }, { withCredentials: true }));
    this._token.set(env.data.accessToken);
    this._member.set(env.data.member);
    await this.refreshMe();
  }

  async logout(): Promise<void> {
    try { await firstValueFrom(this.http.post<void>('/api/auth/logout', null, { withCredentials: true })); } catch { /* ignore */ }
    this.clear();
  }

  async restore(): Promise<void> {
    try {
      const env = await firstValueFrom(this.http.post<{ data: { accessToken: string; expiresAt: string } }>('/api/auth/refresh', null, { withCredentials: true }));
      this._token.set(env.data.accessToken);
      await this.refreshMe();
    } catch {
      this.clear();
    }
  }

  setAccessToken(token: string) { this._token.set(token); }
  clear() { this._token.set(null); this._member.set(null); this._memberships.set([]); }

  private async refreshMe(): Promise<void> {
    const env = await firstValueFrom(this.http.get<{ data: MeResponse }>('/api/auth/me'));
    this._member.set(env.data.member);
    this._memberships.set(env.data.memberships);
  }
}
```

### Step 3: authInterceptor
**File:** `client/src/app/core/auth/auth.interceptor.ts`
**Action:** Create
Functional interceptor (`HttpInterceptorFn`):
- If request URL starts with `/api/` and **is not** `/api/auth/login` or `/api/auth/refresh`, clone with `Authorization: Bearer <token>` when token present.
- On a 401 response: if not already retried, call `auth.restore()` (which posts to `/api/auth/refresh` and updates the token). On success, clone the original request with the new token and replay once. On failure, navigate to `/login` and rethrow.
- Use a single-flight pattern (`refreshInProgress` shared Observable) so that 10 concurrent failing requests trigger one refresh, not ten. Use `shareReplay({ bufferSize: 1, refCount: true })` on the in-flight `restore()` promise.

### Step 4: problemDetailsInterceptor
**File:** `client/src/app/core/errors/problem-details.interceptor.ts`
**Action:** Create
Functional interceptor. On `HttpErrorResponse`:
- If `error.headers.get('content-type')?.startsWith('application/problem+json')` → parse `error.error` as `AppError` and rethrow a new `HttpErrorResponse` whose `error` field is the typed `AppError`.
- Else, build a synthetic `AppError` `{ type: 'about:blank', title: 'Network error', status: error.status, detail: error.message }` and rethrow with it.
Order: this interceptor runs **after** `authInterceptor` so that 401-with-refresh still happens before error normalization.

### Step 5: Wire app.config.ts
**File:** `client/src/app/app.config.ts`
**Action:** Modify
```ts
export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor, problemDetailsInterceptor])),
    provideAppInitializer(() => inject(AuthService).restore()),  // refresh-on-load
  ],
};
```
`provideAppInitializer` ensures the app routes only render after the initial `restore()` resolves (so guards see a deterministic auth state).

### Step 6: Specs
**Files:** `auth.service.spec.ts`, `auth.interceptor.spec.ts`, `problem-details.interceptor.spec.ts`
**Action:** Create
- `AuthService`: login flow stores token + member; logout clears; restore happy path; restore failure clears.
- `authInterceptor`: bearer attached; 401 triggers single refresh and replay; second 401 clears state and redirects.
- `problemDetailsInterceptor`: parses problem-details body into `AppError`; falls back for non-problem-details errors.
Use `provideHttpClientTesting` and `HttpTestingController` to script the responses.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `client/src/app/core/errors/app-error.ts` | Create | Typed error shape |
| `client/src/app/core/auth/auth.types.ts` | Create | DTOs mirroring api-spec |
| `client/src/app/core/auth/auth.service.ts` | Create | State + login/logout/restore |
| `client/src/app/core/auth/auth.interceptor.ts` | Create | Bearer attach + 401 refresh-and-replay |
| `client/src/app/core/errors/problem-details.interceptor.ts` | Create | Normalize problem-details |
| `client/src/app/app.config.ts` | Modify | Wire interceptors + initializer |
| `*.spec.ts` (×3) | Create | Coverage of flows |

## Edge Cases & Risks
- **Concurrent 401s during refresh** — single-flight refresh prevents N×refresh storms.
- **Refresh succeeds but `/me` fails** — treat as logout; clear state and navigate to `/login`.
- **Token in memory only** — survives a soft-nav, lost on full reload — restored from the refresh cookie at bootstrap.
- **XSS exposure of in-memory token** — limited blast radius vs `localStorage`; combine with strong CSP later (out of scope here).
- **provideAppInitializer hang** — if `/api/auth/refresh` takes too long, the app blocks on the splash. Add a 5s timeout and proceed unauthenticated on timeout.

## Acceptance Verification
- [ ] After `auth.login(...)` resolves, every `/api/*` request includes `Authorization: Bearer <token>`.
- [ ] A 401 from a non-auth endpoint triggers exactly one refresh; the original request replays once.
- [ ] Two consecutive 401s clear auth state and route to `/login`.
- [ ] Reloading the page (with a valid refresh cookie) restores `authService.isAuthenticated() === true` before routing decides.
- [ ] `problemDetailsInterceptor` produces `AppError` with the right fields; non-problem-details errors fall back to `{ title: "Network error", ... }`.
