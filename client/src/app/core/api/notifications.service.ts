import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { Envelope } from './workspace.types';
import type { PendingActionDto } from './notifications.types';

@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);

  async listPending(): Promise<PendingActionDto[]> {
    const env = await firstValueFrom(this.http.get<Envelope<PendingActionDto[]>>('/api/notifications/pending'));
    return env.data;
  }

  /** Composed URL for the EventSource — JWT goes via `?access_token=` per T-037 shim. */
  streamUrl(accessToken: string): string {
    return `/api/notifications/stream?access_token=${encodeURIComponent(accessToken)}`;
  }
}
