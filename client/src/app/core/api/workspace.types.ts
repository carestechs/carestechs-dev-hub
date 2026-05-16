/**
 * Frontend mirrors of DevHub.Modules.Workspace.DTOs and DevHub.Contracts.Pagination.
 * Keep in lockstep with docs/api-spec.md §Workspace; AuthService unwraps the
 * { data, meta } envelope before handing values back to components.
 */

export type MemberStatus = 'Active' | 'Suspended' | 'Invited';

export interface TeamDto {
  id: string;
  name: string;
  description?: string;
  projectCount: number;
  createdAt: string;
}

export interface MemberDto {
  id: string;
  displayName: string;
  email: string;
  status: MemberStatus;
  createdAt: string;
}

export interface TeamRefDto { id: string; name: string; }
export interface MemberRefDto { id: string; displayName: string; email: string; }

export interface ProjectDto {
  id: string;
  name: string;
  slug: string;
  projectType: string;
  owningTeam: TeamRefDto;
  description?: string;
  inFlightWorkItems: number;
  createdAt: string;
}

export interface ProjectMembershipDto {
  id: string;
  member: MemberRefDto;
  roles: string[];
  createdAt: string;
}

export interface RoleDto {
  id: string;
  key: string;
  name: string;
  description?: string;
  isSystem: boolean;
}

export interface CreateTeamRequest { name: string; description?: string; }
export interface UpdateTeamRequest { name?: string; description?: string; }

export interface InviteMemberRequest { displayName: string; email: string; }
export interface UpdateMemberRequest { displayName?: string; status?: MemberStatus; }

export interface CreateProjectRequest {
  name: string;
  slug: string;
  projectType: string;
  owningTeamId: string;
  description?: string;
}
export interface UpdateProjectRequest {
  name?: string;
  description?: string;
  projectType?: string;
}

export interface AddMembershipRequest { memberId: string; roleKeys: string[]; }
export interface UpdateMembershipRequest { roleKeys: string[]; }

export interface PageRequest {
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDir?: 'asc' | 'desc';
}

export interface PageMeta {
  totalCount: number;
  page: number;
  pageSize: number;
  sortBy?: string;
  sortDir?: 'asc' | 'desc';
}

export interface Envelope<T> { data: T; meta?: unknown; }
export interface PagedEnvelope<T> { data: T[]; meta: PageMeta; }
