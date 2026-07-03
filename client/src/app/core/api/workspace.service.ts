import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  AddMembershipRequest,
  CreateProjectRequest,
  CreateTeamRequest,
  Envelope,
  InviteMemberRequest,
  MemberDto,
  PageRequest,
  PagedEnvelope,
  ProjectDocDto,
  ProjectDocSummaryDto,
  ProjectDto,
  ProjectMembershipDto,
  RoleDto,
  TeamDto,
  UpdateMemberRequest,
  UpdateMembershipRequest,
  UpdateProjectRequest,
  UpdateTeamRequest,
  UpsertDocRequest,
} from './workspace.types';

/**
 * Typed wrapper over every workspace endpoint. Single-call methods return the
 * unwrapped payload; list methods return the full {@link PagedEnvelope} so the
 * caller has access to the pagination meta.
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceService {
  private readonly http = inject(HttpClient);

  // Teams -----------------------------------------------------------------
  listTeams(req: PageRequest = {}): Promise<PagedEnvelope<TeamDto>> {
    return this.getPaged<TeamDto>('/api/teams', req);
  }
  getTeam(id: string): Promise<TeamDto> { return this.getUnwrap<TeamDto>(`/api/teams/${id}`); }
  createTeam(body: CreateTeamRequest): Promise<TeamDto> { return this.postUnwrap<TeamDto, CreateTeamRequest>('/api/teams', body); }
  updateTeam(id: string, body: UpdateTeamRequest): Promise<TeamDto> { return this.patchUnwrap<TeamDto, UpdateTeamRequest>(`/api/teams/${id}`, body); }
  deleteTeam(id: string): Promise<void> { return firstValueFrom(this.http.delete<void>(`/api/teams/${id}`)); }

  // Members ---------------------------------------------------------------
  listMembers(req: PageRequest & { q?: string } = {}): Promise<PagedEnvelope<MemberDto>> {
    return this.getPaged<MemberDto>('/api/members', req);
  }
  getMember(id: string): Promise<MemberDto> { return this.getUnwrap<MemberDto>(`/api/members/${id}`); }
  inviteMember(body: InviteMemberRequest): Promise<MemberDto> { return this.postUnwrap('/api/members', body); }
  updateMember(id: string, body: UpdateMemberRequest): Promise<MemberDto> { return this.patchUnwrap(`/api/members/${id}`, body); }
  deleteMember(id: string): Promise<void> { return firstValueFrom(this.http.delete<void>(`/api/members/${id}`)); }

  // Projects --------------------------------------------------------------
  listProjects(req: PageRequest & { teamId?: string; projectType?: string } = {}): Promise<PagedEnvelope<ProjectDto>> {
    return this.getPaged<ProjectDto>('/api/projects', req);
  }
  getProject(id: string): Promise<ProjectDto> { return this.getUnwrap<ProjectDto>(`/api/projects/${id}`); }
  getProjectBySlug(slug: string): Promise<ProjectDto> { return this.getUnwrap<ProjectDto>(`/api/projects/by-slug/${encodeURIComponent(slug)}`); }
  createProject(body: CreateProjectRequest): Promise<ProjectDto> { return this.postUnwrap('/api/projects', body); }
  updateProject(id: string, body: UpdateProjectRequest): Promise<ProjectDto> { return this.patchUnwrap(`/api/projects/${id}`, body); }
  deleteProject(id: string): Promise<void> { return firstValueFrom(this.http.delete<void>(`/api/projects/${id}`)); }

  // Memberships -----------------------------------------------------------
  listMemberships(projectId: string): Promise<ProjectMembershipDto[]> {
    return this.getUnwrap<ProjectMembershipDto[]>(`/api/projects/${projectId}/memberships`);
  }
  addMembership(projectId: string, body: AddMembershipRequest): Promise<ProjectMembershipDto> {
    return this.postUnwrap(`/api/projects/${projectId}/memberships`, body);
  }
  updateMembership(projectId: string, membershipId: string, body: UpdateMembershipRequest): Promise<ProjectMembershipDto> {
    return this.patchUnwrap(`/api/projects/${projectId}/memberships/${membershipId}`, body);
  }
  removeMembership(projectId: string, membershipId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/projects/${projectId}/memberships/${membershipId}`));
  }

  // Project Docs ----------------------------------------------------------
  listDocs(projectId: string): Promise<ProjectDocSummaryDto[]> {
    return this.getUnwrap<ProjectDocSummaryDto[]>(`/api/projects/${projectId}/docs`);
  }
  getDoc(projectId: string, key: string): Promise<ProjectDocDto> {
    return this.getUnwrap<ProjectDocDto>(`/api/projects/${projectId}/docs/${key}`);
  }
  upsertDoc(projectId: string, key: string, body: UpsertDocRequest): Promise<ProjectDocDto> {
    return this.putUnwrap<ProjectDocDto, UpsertDocRequest>(`/api/projects/${projectId}/docs/${key}`, body);
  }

  // Roles -----------------------------------------------------------------
  listRoles(): Promise<RoleDto[]> { return this.getUnwrap<RoleDto[]>('/api/roles'); }

  // Helpers ---------------------------------------------------------------
  private async getPaged<T>(url: string, req: PageRequest): Promise<PagedEnvelope<T>> {
    return firstValueFrom(this.http.get<PagedEnvelope<T>>(url, { params: this.toParams(req as Record<string, unknown>) }));
  }
  private async getUnwrap<T>(url: string): Promise<T> {
    const env = await firstValueFrom(this.http.get<Envelope<T>>(url));
    return env.data;
  }
  private async postUnwrap<T, B>(url: string, body: B): Promise<T> {
    const env = await firstValueFrom(this.http.post<Envelope<T>>(url, body));
    return env.data;
  }
  private async patchUnwrap<T, B>(url: string, body: B): Promise<T> {
    const env = await firstValueFrom(this.http.patch<Envelope<T>>(url, body));
    return env.data;
  }
  private async putUnwrap<T, B>(url: string, body: B): Promise<T> {
    const env = await firstValueFrom(this.http.put<Envelope<T>>(url, body));
    return env.data;
  }
  private toParams(req: Record<string, unknown>): HttpParams {
    let params = new HttpParams();
    for (const [k, v] of Object.entries(req)) {
      if (v === undefined || v === null || v === '') continue;
      params = params.set(k, String(v));
    }
    return params;
  }
}
