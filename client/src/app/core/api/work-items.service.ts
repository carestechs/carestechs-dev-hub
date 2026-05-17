import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { Envelope, PagedEnvelope, PageRequest } from './workspace.types';
import type {
  CheckpointContractView,
  CheckpointSignalDto,
  SignalRequest,
  StartWorkItemRequest,
  UpdateWorkItemRequest,
  WorkItemDto,
  WorkItemSummaryDto,
} from './work-items.types';

@Injectable({ providedIn: 'root' })
export class WorkItemsService {
  private readonly http = inject(HttpClient);

  list(projectId: string, req: PageRequest & { status?: string; waitingOnMe?: boolean } = {}): Promise<PagedEnvelope<WorkItemSummaryDto>> {
    return firstValueFrom(this.http.get<PagedEnvelope<WorkItemSummaryDto>>(
      `/api/projects/${projectId}/work-items`,
      { params: this.toParams(req as Record<string, unknown>) },
    ));
  }
  get(projectId: string, id: string): Promise<WorkItemDto> {
    return this.unwrapGet(`/api/projects/${projectId}/work-items/${id}`);
  }
  start(projectId: string, body: StartWorkItemRequest): Promise<WorkItemDto> {
    return this.unwrapPost(`/api/projects/${projectId}/work-items`, body);
  }
  update(projectId: string, workItemId: string, body: UpdateWorkItemRequest): Promise<WorkItemDto> {
    return firstValueFrom(this.http.patch<Envelope<WorkItemDto>>(
      `/api/projects/${projectId}/work-items/${workItemId}`,
      body,
    )).then(env => env.data);
  }
  getCheckpoint(projectId: string, workItemId: string, key: string): Promise<CheckpointContractView> {
    return this.unwrapGet<CheckpointContractView>(
      `/api/projects/${projectId}/work-items/${workItemId}/checkpoints/${encodeURIComponent(key)}`);
  }
  signal(projectId: string, workItemId: string, key: string, body: SignalRequest, idempotencyKey: string): Promise<WorkItemDto> {
    return firstValueFrom(this.http.post<Envelope<WorkItemDto>>(
      `/api/projects/${projectId}/work-items/${workItemId}/checkpoints/${encodeURIComponent(key)}/signal`,
      body,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )).then(env => env.data);
  }
  listSignals(projectId: string, workItemId: string, req: PageRequest = {}): Promise<PagedEnvelope<CheckpointSignalDto>> {
    return firstValueFrom(this.http.get<PagedEnvelope<CheckpointSignalDto>>(
      `/api/projects/${projectId}/work-items/${workItemId}/signals`,
      { params: this.toParams(req as Record<string, unknown>) },
    ));
  }
  cancel(projectId: string, workItemId: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/projects/${projectId}/work-items/${workItemId}/cancel`, null));
  }
  streamUrl(projectId: string, workItemId: string, accessToken: string): string {
    return `/api/projects/${projectId}/work-items/${workItemId}/stream?access_token=${encodeURIComponent(accessToken)}`;
  }

  private async unwrapGet<T>(url: string): Promise<T> {
    const env = await firstValueFrom(this.http.get<Envelope<T>>(url));
    return env.data;
  }
  private async unwrapPost<T, B>(url: string, body: B): Promise<T> {
    const env = await firstValueFrom(this.http.post<Envelope<T>>(url, body));
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
