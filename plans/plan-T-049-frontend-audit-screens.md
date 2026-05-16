# Implementation Plan: T-049 — Audit log screens (project + admin)

## Task Reference
- **Task ID:** T-049 · **Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M
- **Rationale:** UI Spec §8 — filterable audit table with expandable details. Project members need visibility into project decisions; operators need workspace-wide.

## Overview
Three deliverables:
1. `AuditService` (sibling to `WorkspaceService`/`WorkItemsService`).
2. `AuditTable` shared component with filter bar + row expand.
3. Two pages (`ProjectAuditPage` at `/projects/:slug/audit`, `OperatorAuditPage` at `/operator/audit`).

## Implementation Steps

### Step 0: Mockup
**File:** `mockups/audit-log.html` · Create

Variants: empty state, populated mixed-outcome table, expanded row with JSON tree, filter bar (acting member dropdown, action text input, outcome pill bar, date-range pickers), 403 forbidden state.

### Step 1: Types + service
**Files (Create):**
- `client/src/app/core/api/audit.types.ts`

```ts
export type AuditOutcome = 'Granted' | 'Denied' | 'Failed';

export interface AuditActorDto { id: string; displayName: string; }

export interface AuditEntryDto {
  id: string;
  occurredAt: string;
  actingMember?: AuditActorDto;
  projectId?: string;
  targetType: string;
  targetId?: string;
  action: string;
  outcome: AuditOutcome;
  reason?: string;
  details?: unknown;
}

export interface AuditFilter {
  actingMemberId?: string;
  targetType?: string;
  action?: string;
  outcome?: AuditOutcome;
  projectId?: string;
  from?: string;
  to?: string;
}
```

- `client/src/app/core/api/audit.service.ts`

```ts
@Injectable({ providedIn: 'root' })
export class AuditService {
  private readonly http = inject(HttpClient);

  listProject(projectId: string, filter: AuditFilter, page: PageRequest = {}): Promise<PagedEnvelope<AuditEntryDto>> {
    return firstValueFrom(this.http.get<PagedEnvelope<AuditEntryDto>>(
      `/api/projects/${projectId}/audit`,
      { params: this.toParams({ ...filter, ...page }) },
    ));
  }
  listAdmin(filter: AuditFilter, page: PageRequest = {}): Promise<PagedEnvelope<AuditEntryDto>> {
    return firstValueFrom(this.http.get<PagedEnvelope<AuditEntryDto>>(
      '/api/admin/audit',
      { params: this.toParams({ ...filter, ...page }) },
    ));
  }
  private toParams(req: Record<string, unknown>): HttpParams { /* same shape as WorkspaceService.toParams */ }
}
```

### Step 2: AuditTable component
**Files (Create):**
- `client/src/app/features/audit/audit-table.{ts,html,spec.ts}`

Inputs:
- `rows: AuditEntryDto[]`
- `loading: boolean`
- `meta: PageMeta | null`
- `filter: AuditFilter`
- `showFilters: boolean` (default true; the dashboard uses `false` for compact mode)
- `actingMembers: { id, displayName }[]` (dropdown source)

Outputs:
- `filterChanged: AuditFilter`
- `pageChanged: PageChange`

Internal: `expandedIds = signal<Set<string>>(new Set())`. Click row → toggle. Expanded row renders `<pre>` of pretty-printed `details` JSON.

Filter bar: action text input + outcome pill bar + from/to date pickers + acting-member select. Each change debounces 250ms then emits `filterChanged`.

Outcome pill classes:
- Granted → `bg-emerald-50 text-emerald-700`
- Denied → `bg-red-50 text-red-700`
- Failed → `bg-amber-50 text-amber-800`

### Step 3: Project audit page
**Files (Create):**
- `client/src/app/features/audit/project-audit.page.{ts,html,spec.ts}`

Reads `:slug` from `ActivatedRoute`; loads project via `WorkspaceService.getProjectBySlug`. Then parallel loads `AuditService.listProject(projectId, {}, { page: 1, pageSize: 50 })` + `WorkspaceService.listMembers({ pageSize: 200 })` (for the acting-member dropdown). Renders the page header (back to project), then `<audit-table>`.

403 → friendly forbidden page; 404 → "Project not found."

### Step 4: Operator audit page
**Files (Create):**
- `client/src/app/features/audit/operator-audit.page.{ts,html,spec.ts}`

`operatorGuard`. Loads admin audit + member list. Same `<audit-table>`, no project context. Renders breadcrumb "Operator › Audit log."

### Step 5: Routes + Project home link
**File:** `client/src/app/app.routes.ts` · Modify

```ts
{ path: 'projects/:slug/audit',
  loadComponent: () => import('./features/audit/project-audit.page').then(m => m.ProjectAuditPage) },
{ path: 'operator/audit',
  canActivate: [operatorGuard],
  loadComponent: () => import('./features/audit/operator-audit.page').then(m => m.OperatorAuditPage) },
```

**File:** `client/src/app/features/projects/project-home.page.html` · Modify
Replace the aria-disabled "Audit" span with a real `<a routerLink="...">Audit</a>` to `/projects/:slug/audit`.

### Step 6: Specs
- `audit-table.spec.ts`: rows render; outcome pill classes; filter change emits debounced; row expand toggles details.
- `project-audit.page.spec.ts`: parallel load, 403 forbidden surface.
- `operator-audit.page.spec.ts`: loads admin audit, renders rows.

## Files Affected
| File | Action |
|------|--------|
| `mockups/audit-log.html` | Create |
| `core/api/audit.{types,service}.ts` | Create |
| `features/audit/audit-table.{ts,html,spec.ts}` | Create |
| `features/audit/project-audit.page.{ts,html,spec.ts}` | Create |
| `features/audit/operator-audit.page.{ts,html,spec.ts}` | Create |
| `app.routes.ts` | Modify |
| `features/projects/project-home.page.html` | Modify (link the Audit tab) |

## Edge Cases & Risks
- **Acting-member dropdown size.** v1 fetches the first 200 members. Workspaces with more need search-on-type — defer.
- **`details` shape.** Opaque JSON. Pretty-print as `<pre>` for v1; v2 could ship a tree view.
- **Filter persistence.** Filters reset on navigation. URL query-param persistence is a v2 polish.

## Acceptance Verification
- [ ] Mockup approved.
- [ ] `ng build` clean.
- [ ] `ng test` is green; ≥5 new specs.
- [ ] Manual smoke: log in as operator, view `/operator/audit`, expand a row, apply outcome=Denied filter.
