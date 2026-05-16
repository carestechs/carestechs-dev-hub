import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { PagedEnvelope, PageRequest } from './workspace.types';
import type { AuditEntryDto, AuditFilter } from './audit.types';

@Injectable({ providedIn: 'root' })
export class AuditService {
  private readonly http = inject(HttpClient);

  listProject(projectId: string, filter: AuditFilter = {}, page: PageRequest = {}): Promise<PagedEnvelope<AuditEntryDto>> {
    return firstValueFrom(this.http.get<PagedEnvelope<AuditEntryDto>>(
      `/api/projects/${projectId}/audit`,
      { params: this.toParams({ ...filter, ...page }) },
    ));
  }

  listAdmin(filter: AuditFilter = {}, page: PageRequest = {}): Promise<PagedEnvelope<AuditEntryDto>> {
    return firstValueFrom(this.http.get<PagedEnvelope<AuditEntryDto>>(
      '/api/admin/audit',
      { params: this.toParams({ ...filter, ...page }) },
    ));
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
