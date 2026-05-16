import { computed, inject, Injectable, signal } from '@angular/core';
import { NotificationsService } from '../api/notifications.service';
import type { PendingActionDto, PendingActionEvent } from '../api/notifications.types';
import { AuthService } from '../auth/auth.service';

/**
 * Session-long, process-wide store backing the "Pending on you" surfaces (Home list,
 * sidebar group, header badge). Maintains a live signal-backed list driven by
 * <c>NotificationsService</c>'s JSON list + SSE stream.
 *
 * v1 reconnect strategy: on any `onerror`, the EventSource's built-in 3s backoff drives
 * reconnection. A user-initiated <c>reconnect()</c> button is provided for the cold-disconnect
 * case (e.g. 401 after access-token expiry). On every (re)connect, the store refetches
 * <c>listPending()</c> as source of truth (no replay queue server-side).
 */
@Injectable({ providedIn: 'root' })
export class PendingActionsStore {
  private readonly notifications = inject(NotificationsService);
  private readonly auth = inject(AuthService);

  private readonly _list = signal<PendingActionDto[]>([]);
  readonly list = this._list.asReadonly();
  readonly count = computed(() => this._list().length);

  /** Header-friendly badge text — `null` hides the badge, `"99+"` for overflow. */
  readonly badgeText = computed<string | null>(() => {
    const n = this.count();
    if (n === 0) return null;
    if (n >= 100) return '99+';
    return String(n);
  });

  /** True while the SSE stream is connected; flips to false on `onerror`. */
  readonly connected = signal(false);
  /** True while the store is actively running its initial fetch. */
  readonly loading = signal(false);

  private source: EventSource | null = null;

  /** Bootstraps the store — call once from the authed shell on mount. */
  async start(): Promise<void> {
    await this.refresh();
    this.openStream();
  }

  /** Force a re-pull of the JSON list. Used on connect and on user-initiated reconnect. */
  async refresh(): Promise<void> {
    this.loading.set(true);
    try {
      this._list.set(await this.notifications.listPending());
    } finally {
      this.loading.set(false);
    }
  }

  /** Closes any open stream, refetches the list, and reopens. */
  async reconnect(): Promise<void> {
    this.closeStream();
    await this.start();
  }

  /** Logout hook — close the stream and clear local state. */
  shutdown(): void {
    this.closeStream();
    this._list.set([]);
    this.connected.set(false);
  }

  private openStream(): void {
    const token = this.auth.token();
    if (!token) return;
    const url = this.notifications.streamUrl(token);

    let source: EventSource;
    try {
      source = new EventSource(url);
    } catch {
      this.connected.set(false);
      return;
    }
    this.source = source;

    source.onopen = () => this.connected.set(true);
    source.onmessage = ev => this.handleEvent(ev.data);
    source.onerror = () => this.connected.set(false);
  }

  private handleEvent(raw: unknown): void {
    let parsed: PendingActionEvent | null = null;
    try { parsed = typeof raw === 'string' ? JSON.parse(raw) as PendingActionEvent : null; }
    catch { return; }
    if (!parsed || (parsed.kind !== 'raised' && parsed.kind !== 'dismissed')) return;

    if (parsed.kind === 'dismissed') {
      this._list.update(arr => arr.filter(a =>
        !(a.workItemId === parsed.workItemId && a.checkpointKey === parsed.checkpointKey)));
      return;
    }

    // "raised": the event payload doesn't carry display fields (workItemTitle, projectSlug,
    // checkpointDisplayName). Refetch the list to pull them in.
    void this.refresh();
  }

  private closeStream(): void {
    if (this.source) {
      this.source.close();
      this.source = null;
    }
    this.connected.set(false);
  }
}
