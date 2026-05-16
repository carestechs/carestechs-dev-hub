# Implementation Plan: T-046 — PendingActionList + sidebar badge + live SSE

## Task Reference
- **Task ID:** T-046 · **Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M
- **Rationale:** UI Spec §2 (Home) + §App Shell. Without this UI, the backend stream isn't reachable.

## Overview
Three deliverables:
1. `NotificationsService` (sibling to `WorkItemsService`).
2. Session-long SSE wiring in `AppShell` that maintains a singleton `pendingActions` signal app-wide.
3. `PendingActionList` on Home + sidebar group with header count badge.

## Implementation Steps

### Step 0: Mockup
**File:** `mockups/home-pending-actions.html` · Create

Variants: empty state, list with 3+ entries (project chip + work item title + checkpoint key chip), disconnected state (gray indicator + Reconnect button), header badge variations (0 / 3 / 99+).

### Step 1: Types + service
**Files (Create):**
- `client/src/app/core/api/notifications.types.ts`

```ts
export interface PendingActionDto {
  projectId: string;
  projectSlug: string;
  workItemId: string;
  workItemTitle: string;
  checkpointKey: string;
  checkpointDisplayName: string;
  raisedAt: string;
}
```

- `client/src/app/core/api/notifications.service.ts`

```ts
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  async listPending(): Promise<PendingActionDto[]> {
    const env = await firstValueFrom(this.http.get<Envelope<PendingActionDto[]>>('/api/notifications/pending'));
    return env.data;
  }
  streamUrl(accessToken: string): string {
    return `/api/notifications/stream?access_token=${encodeURIComponent(accessToken)}`;
  }
}
```

### Step 2: Session-long SSE in AppShell
**File:** `client/src/app/core/layouts/app-shell/app-shell.ts` · Modify

Add a `pendingActions = signal<PendingActionDto[]>([])` exposed via DI (move to a dedicated `PendingActionsStore` injectable so HomePage + sidebar both read it).

**File:** `client/src/app/core/notifications/pending-actions.store.ts` · Create

```ts
@Injectable({ providedIn: 'root' })
export class PendingActionsStore {
  private readonly notifications = inject(NotificationsService);
  private readonly auth = inject(AuthService);

  private readonly _list = signal<PendingActionDto[]>([]);
  readonly list = this._list.asReadonly();
  readonly count = computed(() => this._list().length);
  readonly badgeText = computed(() => {
    const n = this.count();
    if (n === 0) return null;
    if (n >= 100) return '99+';
    return String(n);
  });

  readonly connected = signal(true);

  private source: EventSource | null = null;

  async start(): Promise<void> {
    await this.refresh();
    this.openStream();
  }

  async refresh(): Promise<void> {
    this._list.set(await this.notifications.listPending());
  }

  reconnect(): void {
    this.closeStream();
    void this.start();
  }

  private openStream(): void {
    const token = this.auth.token();
    if (!token) return;
    this.source = new EventSource(this.notifications.streamUrl(token));
    this.source.onopen = () => this.connected.set(true);
    this.source.onmessage = ev => {
      const parsed = safeParse(ev.data);
      if (!parsed) return;
      if (parsed.kind === 'raised') {
        // Refetch on raised so the DTO carries display fields.
        void this.refresh();
      } else if (parsed.kind === 'dismissed') {
        this._list.update(arr => arr.filter(a => !(a.workItemId === parsed.workItemId && a.checkpointKey === parsed.checkpointKey)));
      }
    };
    this.source.onerror = () => this.connected.set(false);
  }

  private closeStream(): void { this.source?.close(); this.source = null; }
}

function safeParse(s: unknown): { kind: string; workItemId: string; checkpointKey: string } | null {
  try { return typeof s === 'string' ? JSON.parse(s) : null; } catch { return null; }
}
```

`AppShell.ngOnInit` calls `store.start()`. On destroy, `store.closeStream()`.

### Step 3: Home page integration
**File:** `client/src/app/features/home/home.page.html` (and `.ts`) · Modify
Replace the FEAT-001 placeholder with `<pending-action-list />`.

**Files (Create):**
- `client/src/app/features/home/pending-action-list.{ts,html,spec.ts}`

`PendingActionList` reads from `inject(PendingActionsStore)`; renders the list grouped by project; each row links to `/projects/{slug}/work-items/{id}/review`. Empty state when `list().length === 0`.

### Step 4: Sidebar live group + header badge
**File:** `client/src/app/core/layouts/app-shell/sidebar.html` · Modify

Add a "Pending on you" group above the existing nav. Render the first 5 entries grouped by project (client-side `groupBy(projectSlug)`); "See all →" links to Home (`/`).

**File:** `client/src/app/core/layouts/app-shell/app-shell.html` · Modify

Add the badge next to the user menu / app title:
```html
@if (store.badgeText(); as badge) {
  <span class="inline-flex items-center justify-center rounded-full bg-amber-500 text-white text-xs font-medium px-2 py-0.5">{{ badge }}</span>
}
```

### Step 5: Specs
- `pending-action-list.spec.ts`: provide a stub `PendingActionsStore` via `{ provide: PendingActionsStore, useValue: ... }`, render with empty list (empty state) and with 3 entries (rows + links).
- Update `app-shell.spec.ts` (if it exists; otherwise create) to flush the initial `listPending` + assert the badge appears with `n === 3`.
- `pending-actions.store.spec.ts`: stub `EventSource` (same pattern as T-040's `StreamFeed`), assert `raised` event triggers refresh, `dismissed` event mutates the list, `onerror` flips `connected` to false.

## Files Affected
| File | Action |
|------|--------|
| `mockups/home-pending-actions.html` | Create |
| `core/api/notifications.{types,service}.ts` | Create |
| `core/notifications/pending-actions.store.ts` | Create |
| `core/layouts/app-shell/{app-shell.ts,html,spec.ts}` | Modify |
| `core/layouts/app-shell/sidebar.{html,ts}` | Modify |
| `features/home/home.page.{ts,html}` | Modify |
| `features/home/pending-action-list.{ts,html,spec.ts}` | Create |
| `features/home/home.page.spec.ts` | Modify (optional — store is mocked) |
| `core/notifications/pending-actions.store.spec.ts` | Create |

## Edge Cases & Risks
- **EventSource auto-reconnect.** Browser handles transient errors with a 3s backoff. We don't disable it. The manual Reconnect button is for fatal cases (401 after token expiry).
- **`raised` event refetches the full list.** Pragmatic: the SSE payload doesn't carry display fields. v2 could expand to include them and update in-place.
- **Stream survives logout?** No — `closeStream()` runs on logout via `AuthService.logout` → `store.closeStream()`. Document the hook.
- **Multiple tabs.** Each tab opens its own EventSource. Backend allows multiple subscribers per member; documented in T-044.
- **Stream connects before token is restored.** `store.start()` is called from `AppShell.ngOnInit`, which fires only when the authed shell mounts (after `restore()` in app initializer). Safe.

## Acceptance Verification
- [ ] Mockup approved.
- [ ] `ng build` clean.
- [ ] `ng test` is green; new spec count ≥ 5.
- [ ] Manual smoke: log in as operator with a `WaitingOnCheckpoint` work item; the badge shows count, the sidebar group lists entries, the Home page shows the full list. Sign out and back in: the count restores via `listPending()`.
