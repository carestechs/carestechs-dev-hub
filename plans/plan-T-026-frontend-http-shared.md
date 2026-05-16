# Implementation Plan: T-026 — Frontend HTTP layer + shared list/modal components

## Task Reference
- **Task ID:** T-026
- **Type:** Frontend
- **Workflow:** mockup-first
- **Complexity:** L
- **Rationale:** Five admin screens + two project screens land in T-027 and T-028. Without `AppTable`/`AppModal`/`ConfirmDialog` and a typed `workspace.service.ts`, each screen would diverge.

## Overview
Three shared components (`AppTable`, `AppModal`, `ConfirmDialog`) + typed HTTP wrappers around every workspace endpoint + a mockup that captures the table's five states (default, loading, empty, error, paginated).

## Implementation Steps

### Step 0: Mockup
**File:** `mockups/app-table.html`
**Action:** Create

A single page with five `AppCard` containers stacked: default (rows), loading (skeleton), empty (`EmptyState` inside the table area), error (`AppErrorBanner` inside), paginated (footer with prev/next + page indicator). Tailwind CDN, static HTML, copy-paste palette from `docs/ui-specification.md` § Design System.

### Step 1: Types
**File:** `client/src/app/core/api/workspace.types.ts`
**Action:** Create

```typescript
export interface TeamDto { id: string; name: string; description?: string; projectCount: number; createdAt: string; }
export interface MemberDto { id: string; displayName: string; email: string; status: 'Active' | 'Suspended' | 'Invited'; createdAt: string; }
export interface ProjectDto {
  id: string; name: string; slug: string; projectType: string;
  owningTeam: { id: string; name: string };
  description?: string; inFlightWorkItems: number; createdAt: string;
}
export interface ProjectMembershipDto {
  id: string;
  member: { id: string; displayName: string; email: string };
  roles: string[];
  createdAt: string;
}
export interface RoleDto { id: string; key: string; name: string; description?: string; isSystem: boolean; }

export interface PageRequest { page?: number; pageSize?: number; sortBy?: string; sortDir?: 'asc' | 'desc'; }
export interface PageMeta { totalCount: number; page: number; pageSize: number; sortBy?: string; sortDir?: 'asc' | 'desc'; }
export interface Envelope<T> { data: T; meta?: unknown; }
export interface PagedEnvelope<T> { data: T[]; meta: PageMeta; }
```

### Step 2: workspace.service.ts
**File:** `client/src/app/core/api/workspace.service.ts`
**Action:** Create

Typed methods around every endpoint in api-spec §Workspace, returning Promises (we use `firstValueFrom` to keep the SPA's async surface uniform with `AuthService`):

```typescript
@Injectable({ providedIn: 'root' })
export class WorkspaceService {
  private readonly http = inject(HttpClient);

  // Teams
  listTeams(req: PageRequest = {}): Promise<PagedEnvelope<TeamDto>> { return this.getPaged('/api/teams', req); }
  createTeam(body: { name: string; description?: string }): Promise<TeamDto> { return this.postUnwrap('/api/teams', body); }
  updateTeam(id: string, body: { name?: string; description?: string }): Promise<TeamDto> { return this.patchUnwrap(`/api/teams/${id}`, body); }
  deleteTeam(id: string): Promise<void> { return firstValueFrom(this.http.delete<void>(`/api/teams/${id}`)); }

  // Members ... Projects ... Memberships ... Roles ...

  // Helpers
  private async getPaged<T>(url: string, req: PageRequest): Promise<PagedEnvelope<T>> {
    const params = this.pageParams(req);
    const env = await firstValueFrom(this.http.get<PagedEnvelope<T>>(url, { params }));
    return env;
  }
  private async postUnwrap<T, B>(url: string, body: B): Promise<T> {
    const env = await firstValueFrom(this.http.post<Envelope<T>>(url, body));
    return env.data;
  }
  // ... patchUnwrap, getUnwrap, pageParams ...
}
```

Every method strips the `{ data, meta }` envelope. List methods return the full `PagedEnvelope<T>` so callers get pagination meta.

### Step 3: AppTable
**Files:**
- `client/src/app/shared/components/app-table/app-table.{ts,html,spec.ts}`
- `client/src/app/shared/components/app-table/app-table.types.ts`
**Action:** Create

`AppTable<TRow>` is generic via template typing:
- Inputs: `columns: ColumnDef<TRow>[]`, `rows: TRow[]`, `meta: PageMeta | null`, `loading: boolean`, `error: AppError | null`.
- Outputs: `sortChanged: EventEmitter<{ sortBy: string; sortDir: 'asc'|'desc' }>`, `pageChanged: EventEmitter<{ page: number; pageSize: number }>`, `rowClicked: EventEmitter<TRow>`.
- Template hierarchy: AppCard shell → header row (sortable headers w/ chevrons) → body (skeleton rows when `loading`, empty-state when no rows + no loading, error banner when `error`) → footer (page meta + prev/next).
- `ColumnDef<TRow>` shape: `{ id: string; header: string; cell: (row: TRow) => string | TemplateRef<...>; sortable?: boolean; align?: 'left'|'right'|'center'; widthClass?: string }`.
- Cells render via `*ngTemplateOutlet` for `TemplateRef` cells, plain text otherwise. Consumers pass templates via `@ContentChild` named refs (`#actions`, etc.).

### Step 4: AppModal
**Files:** `client/src/app/shared/components/app-modal/app-modal.{ts,html,spec.ts}`
**Action:** Create

Inputs: `open: boolean`, `title: string`, `dismissOnOverlayClick: boolean = true`, `width: 'sm' | 'md' | 'lg' = 'md'`. Outputs: `close: EventEmitter<void>`.

Template:
- Backdrop: `<div class="fixed inset-0 z-40 bg-slate-900/40 backdrop-blur-sm" (click)="dismissOnOverlayClick && close.emit()">` (when `open`).
- Panel: `<div class="fixed inset-0 z-50 flex items-start justify-center p-6 overflow-y-auto" role="dialog" aria-modal="true">` with the `AppCard` shell inside.
- Sections: header (title + close button), body (`<ng-content>`), footer (`<ng-content select="[modal-footer]">`).

Focus management:
- On open: store `document.activeElement`, focus the first focusable child of the panel.
- On close: restore the stored element.
- Escape key: emit `close`.
- Tab/Shift+Tab: cycle focus within the panel (maintain a `focusable()` selector list).

### Step 5: ConfirmDialog
**Files:** `client/src/app/shared/components/confirm-dialog/confirm-dialog.{ts,html,spec.ts}`
**Action:** Create

Thin wrapper around AppModal:
- Inputs: `open`, `title`, `message`, `confirmLabel = 'Confirm'`, `cancelLabel = 'Cancel'`, `variant: 'danger' | 'primary' = 'danger'`, `working: boolean = false`.
- Outputs: `confirmed: EventEmitter<void>`, `cancelled: EventEmitter<void>`.

Template renders the message as the modal body and two AppButtons (primary `Confirm` with `loading=working`, secondary `Cancel`) in the footer.

### Step 6: Barrel
**File:** `client/src/app/shared/index.ts`
**Action:** Modify

```typescript
export * from './components/app-table/app-table';
export * from './components/app-table/app-table.types';
export * from './components/app-modal/app-modal';
export * from './components/confirm-dialog/confirm-dialog';
```

### Step 7: Tests
**Files:** `*.spec.ts` for each new component + `workspace.service.spec.ts`
**Action:** Create

- AppTable: renders columns + cells, emits `sortChanged` on header click, emits `pageChanged` on footer click, shows skeleton when `loading`, shows empty state when `rows.length === 0 && !loading`, renders error banner when `error` set.
- AppModal: focus trap cycles, Escape emits close, overlay click respects `dismissOnOverlayClick`, restores focus on close.
- ConfirmDialog: emits confirmed on primary click, cancelled on secondary or overlay/Escape.
- WorkspaceService: stubs HttpClient with `HttpTestingController`, asserts paged params, unwrapping, error pass-through.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `mockups/app-table.html` | Create | 5-state mockup |
| `client/src/app/core/api/workspace.types.ts` | Create | TS mirror of api-spec DTOs |
| `client/src/app/core/api/workspace.service.ts` | Create | Typed HTTP wrappers |
| `client/src/app/shared/components/app-table/*` | Create | AppTable |
| `client/src/app/shared/components/app-modal/*` | Create | AppModal |
| `client/src/app/shared/components/confirm-dialog/*` | Create | ConfirmDialog |
| `client/src/app/shared/index.ts` | Modify | Export the new components |
| `*.spec.ts` (×4) | Create | Component + service specs |

## Edge Cases & Risks
- **Focus trap edge case** — a modal with no focusable children. Skip focus on open; Escape still works.
- **Sortable header chevrons** — three states (no sort / asc / desc). The "no sort" state for the active column shouldn't appear; only `asc`/`desc` toggles.
- **Column generic typing** — TS-only enforced; runtime still tolerates any value passed to `cell`.
- **Pagination overflow** — `meta.page > totalPages` could be supplied by a stale client. AppTable's `pageChanged` clamps to `[1, totalPages]` before emit.

## Acceptance Verification
- [ ] `mockups/app-table.html` covers all 5 states.
- [ ] AppTable: `sortChanged` + `pageChanged` emit with the right payload.
- [ ] AppModal: focus trap + Escape + overlay all behave correctly under test.
- [ ] ConfirmDialog: confirm and cancel both emit; `working=true` disables the confirm button.
- [ ] `WorkspaceService` unwraps `{ data, meta }` and surfaces typed errors via the existing interceptor.
- [ ] All specs pass under `npm run test:ci`.
