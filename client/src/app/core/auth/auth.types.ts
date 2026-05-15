/**
 * Frontend types mirroring DevHub.Api auth contracts. Keep them in lockstep with
 * docs/api-spec.md § Identity — the {data: ...} envelope is unwrapped by AuthService.
 */
export interface Member {
  id: string;
  displayName: string;
  email: string;
}

export interface Membership {
  projectId: string;
  projectSlug: string;
  roles: string[];
}

export interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  member: Member;
}

export interface RefreshResponse {
  accessToken: string;
  expiresAt: string;
}

export interface MeResponse {
  member: Member;
  memberships: Membership[];
}

export interface Envelope<T> {
  data: T;
  meta?: unknown;
}
