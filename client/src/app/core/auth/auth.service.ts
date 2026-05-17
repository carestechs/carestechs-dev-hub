import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, Observable, ReplaySubject } from 'rxjs';
import type {
  Envelope,
  LoginResponse,
  Member,
  Membership,
  MeResponse,
  RefreshResponse,
} from './auth.types';

const OPERATOR_ROLE_KEY = 'operator';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly _token = signal<string | null>(null);
  private readonly _member = signal<Member | null>(null);
  private readonly _memberships = signal<Membership[]>([]);
  /** Workspace-scoped operator grant from /me — true even if memberships is empty. */
  private readonly _isOperatorWorkspace = signal<boolean>(false);

  readonly token = this._token.asReadonly();
  readonly currentMember = this._member.asReadonly();
  readonly memberships = this._memberships.asReadonly();
  readonly isAuthenticated = computed(() => this._token() !== null);

  /**
   * True if the member holds the system <c>operator</c> role at workspace scope
   * OR if any project membership carries the operator role key. The workspace-
   * scoped grant is what /me's isOperator flag surfaces; the per-project check
   * stays as a backstop for the (unusual) case where the operator role is
   * assigned only via ProjectMembership.
   */
  readonly isOperator = computed(() =>
    this._isOperatorWorkspace() ||
    this._memberships().some(m => m.roles.includes(OPERATOR_ROLE_KEY)));

  /** Single-flight refresh — used by the auth interceptor to coalesce N concurrent 401s into one POST /refresh. */
  private refreshInFlight: Observable<string | null> | null = null;

  async login(email: string, password: string): Promise<void> {
    const envelope = await firstValueFrom(
      this.http.post<Envelope<LoginResponse>>(
        '/api/auth/login',
        { email, password },
        { withCredentials: true },
      ),
    );
    this._token.set(envelope.data.accessToken);
    this._member.set(envelope.data.member);
    await this.refreshMeQuiet();
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', null, { withCredentials: true }));
    } catch {
      // Server-side failure should not block local logout.
    }
    this.clear();
  }

  /**
   * Restore session at bootstrap (or after a soft-refresh): present the refresh cookie,
   * receive a fresh access token, then resolve member + memberships.
   */
  async restore(): Promise<void> {
    try {
      const env = await firstValueFrom(
        this.http.post<Envelope<RefreshResponse>>('/api/auth/refresh', null, { withCredentials: true }),
      );
      this._token.set(env.data.accessToken);
      await this.refreshMeQuiet();
    } catch {
      this.clear();
    }
  }

  /**
   * Used by authInterceptor on 401. Coalesces concurrent callers onto a single in-flight
   * refresh and resolves to the new access token (or null on failure).
   */
  ensureFreshAccessToken(): Observable<string | null> {
    if (this.refreshInFlight) return this.refreshInFlight;

    const subject = new ReplaySubject<string | null>(1);
    this.refreshInFlight = subject.asObservable();

    this.http
      .post<Envelope<RefreshResponse>>('/api/auth/refresh', null, { withCredentials: true })
      .subscribe({
        next: env => {
          this._token.set(env.data.accessToken);
          subject.next(env.data.accessToken);
          subject.complete();
          this.refreshInFlight = null;
        },
        error: () => {
          this.clear();
          subject.next(null);
          subject.complete();
          this.refreshInFlight = null;
        },
      });

    return this.refreshInFlight;
  }

  setAccessToken(token: string | null): void {
    this._token.set(token);
  }

  clear(): void {
    this._token.set(null);
    this._member.set(null);
    this._memberships.set([]);
    this._isOperatorWorkspace.set(false);
  }

  private async refreshMeQuiet(): Promise<void> {
    try {
      const env = await firstValueFrom(
        this.http.get<Envelope<MeResponse>>('/api/auth/me'),
      );
      this._member.set(env.data.member);
      this._memberships.set(env.data.memberships);
      this._isOperatorWorkspace.set(env.data.isOperator);
    } catch {
      // If /me fails the token is unusable — treat as logged out.
      this.clear();
    }
  }
}
