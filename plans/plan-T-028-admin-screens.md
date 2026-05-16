# Implementation Plan: T-028 — Admin screens (Teams, Members, Project memberships)

## Task Reference
- **Task ID:** T-028
- **Type:** Frontend
- **Workflow:** mockup-first
- **Complexity:** L
- **Rationale:** Without these screens, operators have to hit the API directly to seed teams/members/memberships — defeating the front-door rule.

## Overview
Three operator-only CRUD screens behind a new `operatorGuard`. Each screen reuses `AppTable` + `AppModal` + `ConfirmDialog` from T-026, surfaces the typed `AppError` from interceptors, and writes via `WorkspaceService`. The sidebar's Admin section becomes clickable for operators.

## Implementation Steps

### Step 0: Mockups
**Files:** `mockups/admin-teams.html`, `mockups/admin-members.html`, `mockups/project-memberships.html`
**Action:** Create

Each mockup mirrors the same shape:
- Page header (h1 + brief description + primary `New …` button).
- `AppTable` (default state) with realistic rows.
- One open modal example (create form) per page, showing labels/inputs.
- Confirm-dialog example (delete confirmation).

### Step 1: operatorGuard
**Files:** `client/src/app/core/auth/operator.guard.{ts,spec.ts}`
**Action:** Create

```typescript
export const operatorGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) return router.parseUrl('/login');
  if (!auth.isOperator()) return router.parseUrl('/');
  return true;
};
```

Spec covers: anonymous → /login; member → /; operator → true.

### Step 2: TeamsPage
**Files:** `client/src/app/features/admin/teams/teams.page.{ts,html,spec.ts}`, `team-form.modal.{ts,html,spec.ts}`
**Action:** Create

```ts
@Component({...})
export class TeamsPage {
  private readonly ws = inject(WorkspaceService);

  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);
  protected readonly teams = signal<TeamDto[]>([]);
  protected readonly meta = signal<PageMeta | null>(null);
  protected readonly editing = signal<TeamDto | null>(null);
  protected readonly modalOpen = signal(false);
  protected readonly toDelete = signal<TeamDto | null>(null);
  protected readonly deleting = signal(false);

  protected readonly columns: ColumnDef<TeamDto>[] = [
    { id: 'name',         header: 'Name',         cell: t => t.name, sortable: true },
    { id: 'description',  header: 'Description',  cell: t => t.description ?? '' },
    { id: 'projectCount', header: 'Projects',     cell: t => String(t.projectCount), align: 'right' },
    { id: 'createdAt',    header: 'Created',      cell: t => fmtDate(t.createdAt), sortable: true },
    { id: 'actions',      header: '',             cell: '@actions', align: 'right' },  // template-ref bound below
  ];

  // load(), onCreate(), onSubmit(), onDelete() — straightforward
}
```

Template wires the `AppTable` with a `<ng-template #actions let-team>` block containing Edit + Delete buttons. Submission of the modal calls `ws.createTeam(...)` or `ws.updateTeam(...)`; failure surfaces `AppError` inside the modal body.

`TeamFormModal`:
- Inputs: `open: boolean`, `team: TeamDto | null`.
- Outputs: `submitted: EventEmitter<{ name: string; description?: string }>`, `cancelled: EventEmitter<void>`.
- Reactive form (`name` required + maxLength 120; `description` maxLength 1000).
- Shows `serverError` (AppError) banner if `submitted` causes a server failure (parent sets via input).

### Step 3: MembersPage + MemberFormModal
**Files:** `client/src/app/features/admin/members/*`
**Action:** Create

Same pattern as TeamsPage. Columns: Display name, Email, Status (badge), Created, Actions. The form modal asks for `displayName` + `email`; member invitations don't set a password (FEAT-002 doesn't ship an onboarding flow — the invite is just creating the record; the invited member's first login would need a separate "set password" flow which lands later).

For v1 the modal also exposes a Status dropdown for editing (Active/Suspended) — useful to suspend a member without an onboarding flow.

### Step 4: Project memberships page
**Files:** `client/src/app/features/projects/memberships/memberships.page.{ts,html,spec.ts}`, `membership-form.modal.{ts,html,spec.ts}`
**Action:** Create

Route: `/projects/:slug/admin/memberships`. Reads `:slug` from the route, fetches the project (for the header) + memberships list + roles list (in parallel via `Promise.all`).

Columns: Member name, Email, Roles (comma-joined or pills), Created, Actions (Edit / Remove).

`MembershipFormModal`:
- Inputs: `open`, `existing: ProjectMembershipDto | null`, `availableRoles: RoleDto[]`, `unassignedMembers: MemberDto[]` (for new membership only).
- Member picker (autocomplete over `unassignedMembers`) on create; read-only on edit.
- Role multi-select using checkboxes per role.
- Submit → `ws.addMembership(projectId, { memberId, roleKeys })` or `ws.updateMembership(...)`.

### Step 5: Routes
**File:** `client/src/app/app.routes.ts`
**Action:** Modify

```ts
{
  path: 'admin/teams',
  canActivate: [operatorGuard],
  loadComponent: () => import('./features/admin/teams/teams.page').then(m => m.TeamsPage),
},
{
  path: 'admin/members',
  canActivate: [operatorGuard],
  loadComponent: () => import('./features/admin/members/members.page').then(m => m.MembersPage),
},
{
  path: 'projects/:slug/admin/memberships',
  canActivate: [operatorGuard],
  loadComponent: () => import('./features/projects/memberships/memberships.page').then(m => m.MembershipsPage),
},
```

### Step 6: Sidebar wiring
**File:** `client/src/app/core/layouts/app-shell/sidebar.html`
**Action:** Modify

The sidebar already shows the Admin group when `isOperator()`. Update the three `<a>` tags from placeholder hrefs to `routerLink="/admin/teams"`, `/admin/members`, etc. Membership management has no top-level entry (it's reachable from the project page).

### Step 7: Specs
**Files:** `*.spec.ts` per page + per modal
**Action:** Create

Each page-level spec covers:
- Load happy path renders rows.
- Empty state renders.
- Server error renders inline (AppErrorBanner).
- Open create modal → submit → list refreshes.
- Click delete → ConfirmDialog → confirm → list refreshes.
- 409 conflict from delete surfaces inline.
- Non-operator caller is redirected by the route guard (spec on operatorGuard, not the page).

Each modal-level spec covers form validation + submit emission.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `mockups/admin-{teams,members}.html`, `mockups/project-memberships.html` | Create | Stakeholder review |
| `client/src/app/core/auth/operator.guard.{ts,spec.ts}` | Create | Functional guard |
| `client/src/app/features/admin/teams/*` | Create | Page + modal + specs |
| `client/src/app/features/admin/members/*` | Create | Page + modal + specs |
| `client/src/app/features/projects/memberships/*` | Create | Page + modal + specs |
| `client/src/app/app.routes.ts` | Modify | Add three admin routes |
| `client/src/app/core/layouts/app-shell/sidebar.html` | Modify | Wire real admin links |

## Edge Cases & Risks
- **Invite without onboarding** — invited members can't log in until someone sets their password. This is acceptable for v1 (only the seed operator exists in practice). A "send invite email with password setup link" flow lands in v1.x.
- **Removing the last operator** — server returns 409; client shows the inline error inside ConfirmDialog. Test that the dialog stays open and the error renders.
- **Role multi-select for v1** — the only role we ship is `operator`. The membership form still renders a checkbox list (future-proof for v2 roles).
- **Project lookup by slug vs id in memberships route** — same gap as T-027. Confirm `GET /api/projects/{slug}` or use `?slug=` resolution.

## Acceptance Verification
- [ ] Three mockups approved.
- [ ] `operatorGuard` redirects non-operators away from `/admin/*` (spec).
- [ ] Each page loads, opens a create modal, submits, refreshes the list, all without page reload.
- [ ] Delete confirmation; 409 surfaces inline inside the ConfirmDialog.
- [ ] Memberships page reads the projectId from the slug and joins roles correctly.
- [ ] Sidebar Admin links navigate to the right routes for an operator; hidden for non-operators.
- [ ] All new specs pass under `npm run test:ci`.
