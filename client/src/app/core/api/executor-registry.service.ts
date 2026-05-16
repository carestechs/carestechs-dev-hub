import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { Envelope, PagedEnvelope, PageRequest } from './workspace.types';
import type {
  CreateBindingRequest,
  CreateExecutorRequest,
  ExecutorBindingDto,
  ExecutorDto,
  ExecutorStatus,
  ReplaceContractsRequest,
  UpdateExecutorRequest,
} from './executor-registry.types';

@Injectable({ providedIn: 'root' })
export class ExecutorRegistryService {
  private readonly http = inject(HttpClient);

  // Executors -------------------------------------------------------------
  listExecutors(req: PageRequest & { status?: ExecutorStatus } = {}): Promise<PagedEnvelope<ExecutorDto>> {
    return firstValueFrom(
      this.http.get<PagedEnvelope<ExecutorDto>>('/api/admin/executors', { params: this.toParams(req as Record<string, unknown>) }),
    );
  }
  getExecutor(id: string): Promise<ExecutorDto> {
    return this.unwrapGet(`/api/admin/executors/${id}`);
  }
  createExecutor(body: CreateExecutorRequest): Promise<ExecutorDto> {
    return this.unwrapPost('/api/admin/executors', body);
  }
  updateExecutor(id: string, body: UpdateExecutorRequest): Promise<ExecutorDto> {
    return this.unwrapPatch(`/api/admin/executors/${id}`, body);
  }
  replaceContracts(id: string, body: ReplaceContractsRequest): Promise<ExecutorDto> {
    return this.unwrapPost(`/api/admin/executors/${id}/checkpoint-contracts`, body);
  }
  deleteExecutor(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/admin/executors/${id}`));
  }

  // Bindings --------------------------------------------------------------
  listBindings(req: PageRequest = {}): Promise<PagedEnvelope<ExecutorBindingDto>> {
    return firstValueFrom(
      this.http.get<PagedEnvelope<ExecutorBindingDto>>('/api/admin/executor-bindings', { params: this.toParams(req as Record<string, unknown>) }),
    );
  }
  createBinding(body: CreateBindingRequest): Promise<ExecutorBindingDto> {
    return this.unwrapPost('/api/admin/executor-bindings', body);
  }
  deleteBinding(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/admin/executor-bindings/${id}`));
  }

  // Helpers ---------------------------------------------------------------
  private async unwrapGet<T>(url: string): Promise<T> {
    const env = await firstValueFrom(this.http.get<Envelope<T>>(url));
    return env.data;
  }
  private async unwrapPost<T, B>(url: string, body: B): Promise<T> {
    const env = await firstValueFrom(this.http.post<Envelope<T>>(url, body));
    return env.data;
  }
  private async unwrapPatch<T, B>(url: string, body: B): Promise<T> {
    const env = await firstValueFrom(this.http.patch<Envelope<T>>(url, body));
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
