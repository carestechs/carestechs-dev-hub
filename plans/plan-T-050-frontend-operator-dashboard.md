# Implementation Plan: T-050 — Operator dashboard

## Task Reference
- **Task ID:** T-050 · **Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M
- **Rationale:** UI Spec §7 — the "routing-layer replacement." Operators see in one place what they previously hunted for in chat.

## Overview
Single page at `/operator`, behind `operatorGuard`. Three panels, all client-side aggregations of existing endpoints:
1. **In-flight totals** — sum of `inFlightWorkItems` across all projects.
2. **Pending approvals (grouped by project)** — from the live `PendingActionsStore` (FEAT-005). Operators see workspace-wide via the reconciler.
3. **Recent audit events** — `AuditService.listAdmin({ outcome: 'Denied'|'Failed', pageSize: 50 })`, rendered via the compact variant of `AuditTable` from T-049.

No new backend.

## Implementation Steps

### Step 0: Mockup
**File:** `mockups/operator-dashboard.html` · Create

Three-card layout: header with title + Refresh button; in-flight totals (big number + per-project breakdown bar); pending approvals (grouped by project, each entry links to review); recent denies/failures (chronological list with outcome pills).

### Step 1: Page component
**Files (Create):**
- `client/src/app/features/operator/operator-dashboard.page.{ts,html,spec.ts}`

```ts
@Component({
  selector: 'operator-dashboard-page',
  standalone: true,
  imports: [RouterLink, AuditTable],
  templateUrl: './operator-dashboard.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperatorDashboardPage implements OnInit {
  private readonly ws = inject(WorkspaceService);
  private readonly audit = inject(AuditService);
  protected readonly pending = inject(PendingActionsStore);

  protected readonly loading = signal(true);
  protected readonly projects = signal<ProjectDto[]>([]);
  protected readonly recentAudit = signal<AuditEntryDto[]>([]);

  protected readonly inFlightTotal = computed(() =>
    this.projects().reduce((acc, p) => acc + p.inFlightWorkItems, 0));

  protected readonly groupedPending = computed(() => {
    const bySlug = new Map<string, PendingActionDto[]>();
    for (const a of this.pending.list()) {
      const arr = bySlug.get(a.projectSlug);
      if (arr) arr.push(a);
      else bySlug.set(a.projectSlug, [a]);
    }
    return Array.from(bySlug, ([slug, actions]) => ({ slug, actions }));
  });

  ngOnInit(): void { void this.refresh(); }

  protected async refresh(): Promise<void> {
    this.loading.set(true);
    try {
      const [projects, audit] = await Promise.all([
        this.ws.listProjects({ page: 1, pageSize: 100 }),
        this.audit.listAdmin({ outcome: undefined /* see below */ }, { page: 1, pageSize: 50 }),
      ]);
      this.projects.set(projects.data);
      this.recentAudit.set(audit.data.filter(a => a.outcome === 'Denied' || a.outcome === 'Failed'));
      // PendingActionsStore is already streaming; trigger one refresh for source-of-truth.
      await this.pending.refresh();
    } finally {
      this.loading.set(false);
    }
  }
}
```

The `outcome` filter on the audit query is intentionally NOT used server-side (the API doesn't yet support `outcome=Denied,Failed` as a list — only a single value). v1 client-side-filters the page of 50; v2 can add multi-value support to the backend.

### Step 2: Template
**File:** `client/src/app/features/operator/operator-dashboard.page.html` · Create

Wraps content in `max-w-7xl mx-auto` (overriding the shell's `max-w-5xl` via Tailwind specificity — see Step 4). Three sections, each a `<section class="bg-white rounded-xl shadow-sm p-6">`.

### Step 3: Route
**File:** `client/src/app/app.routes.ts` · Modify
```ts
{ path: 'operator',
  canActivate: [operatorGuard],
  loadComponent: () => import('./features/operator/operator-dashboard.page').then(m => m.OperatorDashboardPage) },
```

### Step 4: Shell width override
**File:** `client/src/app/core/layouts/app-shell/app-shell.html` · Verify or Modify

Current shell wraps content in `max-w-5xl`. For the dashboard we want wider. v1 pragmatic fix: have the dashboard's outer `<div>` use negative margins to escape and apply its own width — clean Tailwind escape:

```html
<!-- inside operator-dashboard.page.html -->
<div class="mx-auto max-w-7xl -mx-4 md:-mx-6">
  <!-- panels -->
</div>
```

The negative margin cancels the shell's `px-4 md:px-6` padding, and `max-w-7xl` widens the content. Cleaner than mutating the shell. Document inline.

### Step 5: Sidebar link
**File:** `client/src/app/core/layouts/app-shell/sidebar.html` · Verify

The operator group already has `<a routerLink="/operator">Dashboard</a>` and `<a routerLink="/operator/audit">Audit log</a>` placeholder links. Once T-049 and this PR land, both routes exist — the sidebar wiring needs no change.

### Step 6: Specs
**File:** `client/src/app/features/operator/operator-dashboard.page.spec.ts` · Create

Tests:
- Page renders three panels after parallel load (projects + audit + pending).
- In-flight total = sum across projects.
- Recent audit panel filters to Denied/Failed only.
- Pending approvals grouped by projectSlug; clicking an entry routes to review.
- Refresh button re-triggers all three loads.

Provide `PendingActionsStore` via a stub for deterministic state.

## Files Affected
| File | Action |
|------|--------|
| `mockups/operator-dashboard.html` | Create |
| `features/operator/operator-dashboard.page.{ts,html,spec.ts}` | Create |
| `app.routes.ts` | Modify |
| `core/layouts/app-shell/sidebar.html` | Verify (no change) |

## Edge Cases & Risks
- **In-flight count accuracy.** v1 sums `inFlightWorkItems` from the project list. The Project DTO already returns this value (computed server-side in T-024). If the list is paginated at 100 and the operator has more than 100 projects, the total is approximate — flag in v1 docs.
- **Multi-outcome filter.** The audit API takes one outcome at a time. v1 fetches 50 mixed and filters client-side. If denies+failures are rare in the last 50 rows, the panel shows fewer than expected. Documented; v2 backend gain.
- **PendingActionsStore lifecycle.** The store starts on AppShell mount (FEAT-005). The dashboard reads its current state. If the operator visits the dashboard *before* the store's initial fetch resolves, `groupedPending` is briefly empty. Acceptable v1 UX.
- **Refresh storm.** Refresh button = 2 HTTP calls + 1 store sync. No throttling.

## Acceptance Verification
- [ ] Mockup approved.
- [ ] `ng build` clean.
- [ ] `ng test` is green; ≥5 new specs.
- [ ] Manual smoke: log in as operator, navigate to `/operator`, see all three panels populated, refresh, sign out and in.

## FEAT-006 completion gate

After T-050 merges:
- Audit query API live (project + admin).
- Audit log UI live (project + admin variants).
- Operator dashboard live.
- All 5 ACs verifiable end-to-end.

🎉 **All six v1 FEATs complete.** v1 is shippable.
