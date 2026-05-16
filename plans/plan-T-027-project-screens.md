# Implementation Plan: T-027 — Project list + Project home screens

## Task Reference
- **Task ID:** T-027
- **Type:** Frontend
- **Workflow:** mockup-first
- **Complexity:** M
- **Rationale:** Members need to see and open their projects after FEAT-002 lands; without this UI the API surface is invisible.

## Overview
Real project grid at `/projects` (replaces the FEAT-001 empty-state placeholder on Home) and a project home at `/projects/:slug` with header + team + project-type chip + stub work-item area (FEAT-004 fills the work-items table).

## Implementation Steps

### Step 0: Mockups
**Files:** `mockups/project-list.html`, `mockups/project-home.html`
**Action:** Create

`project-list.html`: app-shell chrome + page header ("Projects") + filter bar (Team dropdown, Project type dropdown, search box) + 3-column responsive grid of project cards (slug, name, team, projectType chip, "X in-flight work items").

`project-home.html`: app-shell chrome + project header (name as h1, slug as caption, team owner, projectType chip, "X members" + "X in-flight") + tab bar (`Work items` / `Members` / `Audit`) with `Work items` selected showing `<EmptyState title="Work items land in FEAT-004">`.

### Step 1: ProjectCard
**Files:** `client/src/app/features/projects/project-card.{ts,html,spec.ts}`
**Action:** Create

Standalone, OnPush, AppCard `clickable=true` host. Inputs: `project: ProjectDto`. Output: `opened: EventEmitter<ProjectDto>`. Layout: `<h3>` (name) → caption row (team name + projectType chip) → footer (inFlightWorkItems badge). Spec covers the chip color (sky-100 for default), the click → `opened` emit.

### Step 2: ProjectListPage
**Files:** `client/src/app/features/projects/project-list.page.{ts,html,spec.ts}`
**Action:** Create

```ts
@Component({...})
export class ProjectListPage {
  private readonly ws = inject(WorkspaceService);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);
  protected readonly projects = signal<ProjectDto[]>([]);
  protected readonly meta = signal<PageMeta | null>(null);
  protected readonly filters = signal<{ teamId?: string; projectType?: string; q?: string }>({});

  constructor() { void this.load(); }

  protected async load(req: PageRequest = {}): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const env = await this.ws.listProjects({ sortBy: 'updatedAt', sortDir: 'desc', ...req });
      this.projects.set(env.data);
      this.meta.set(env.meta);
    } catch (e: unknown) {
      this.error.set(toAppError(e));
    } finally { this.loading.set(false); }
  }

  protected onProjectOpen(p: ProjectDto): void {
    void this.router.navigate(['/projects', p.slug]);
  }
}
```

Template: filter bar bound to the `filters` signal (filter changes call `load()` with the new params), then `@for (p of projects()) <project-card [project]="p" (opened)="onProjectOpen($event)" />`, with skeleton cards when `loading` and `<empty-state>` when empty.

### Step 3: ProjectHomePage
**Files:** `client/src/app/features/projects/project-home.page.{ts,html,spec.ts}`
**Action:** Create

```ts
@Component({...})
export class ProjectHomePage {
  private readonly ws = inject(WorkspaceService);
  private readonly route = inject(ActivatedRoute);

  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);
  protected readonly project = signal<ProjectDto | null>(null);

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe(async (params) => {
      const slug = params.get('slug')!;
      void this.load(slug);
    });
  }

  private async load(slug: string): Promise<void> {
    this.loading.set(true); this.error.set(null);
    try { this.project.set(await this.ws.getProjectBySlug(slug)); }
    catch (e: unknown) { this.error.set(toAppError(e)); }
    finally { this.loading.set(false); }
  }
}
```

Template: header (name, slug, team chip, projectType chip), tabs (`Work items` / `Members` / `Audit` — only `Work items` is active in v1), and an `<empty-state>` "Work items land in FEAT-004."

For the project lookup, T-024 must expose a slug-or-id resolution. Confirm `GET /api/projects/{id}` accepts a slug; if not, add a query-string variant `GET /api/projects?slug=X` and use it from `WorkspaceService.getProjectBySlug`.

### Step 4: Forbidden + Not-Found pages
**File:** `client/src/app/features/projects/project-error.page.{ts,html}`
**Action:** Create

One small component that takes a `kind: 'not-found' | 'forbidden'` route data param and renders the documented copy from `docs/ui-specification.md` § State Patterns. Used as the route's `errorComponent` (manual fallback when `load()` throws 403 or 404).

Simpler approach: handle these inline in `ProjectHomePage` via the `error` signal: when `error()?.status === 404` show "Project not found"; when `=== 403` show "You don't have access to this project". Choose the inline path for v1; promote to a shared component if more screens need it.

### Step 5: Routes
**File:** `client/src/app/app.routes.ts`
**Action:** Modify

Inside the `[authGuard]` `AppShell` parent:
```ts
{ path: 'projects', loadComponent: () => import('./features/projects/project-list.page').then(m => m.ProjectListPage) },
{ path: 'projects/:slug', loadComponent: () => import('./features/projects/project-home.page').then(m => m.ProjectHomePage) },
```

### Step 6: Home page tweak
**File:** `client/src/app/features/home/home.page.html`
**Action:** Modify

Replace the "Your projects" `<empty-state>` placeholder with a small "Browse all →" link to `/projects`, and a 3-card preview (top 3 by `updatedAt`). The empty case still shows the original placeholder when `projects()` is empty.

Reuse `WorkspaceService.listProjects` from `HomePage` (no new component).

### Step 7: Specs
**Files:** `*.spec.ts` for each page + card
**Action:** Create

- ProjectListPage: renders loading skeletons → 3 cards after fetch resolves; filter change triggers a new `load()`; empty state when no projects.
- ProjectHomePage: success → renders project name + team chip; 404 → friendly "Project not found"; 403 → friendly "You don't have access."
- ProjectCard: emits `opened` on click; renders chip and counts.
- HomePage: now lists projects when present, falls back to empty state when none.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `mockups/project-list.html` | Create | Stakeholder review |
| `mockups/project-home.html` | Create | Stakeholder review |
| `client/src/app/features/projects/project-card.{ts,html,spec.ts}` | Create | Card component |
| `client/src/app/features/projects/project-list.page.{ts,html,spec.ts}` | Create | List page |
| `client/src/app/features/projects/project-home.page.{ts,html,spec.ts}` | Create | Home page |
| `client/src/app/app.routes.ts` | Modify | Add /projects + /projects/:slug |
| `client/src/app/features/home/home.page.html` | Modify | Replace placeholder with real card grid |
| `client/src/app/features/home/home.page.ts` | Modify | Inject WorkspaceService, load top-3 projects |
| `client/src/app/features/home/home.page.spec.ts` | Modify | Cover the new branches |

## Edge Cases & Risks
- **Slug routing vs id routing** — controllers should accept both. If T-024 only accepts ids, T-027 either adds a `/api/projects/by-slug/{slug}` route or resolves slugs client-side via `listProjects({ q: slug })`. Decide in T-024's review.
- **Race between Home and ProjectList fetches** — both call `listProjects`; HttpClient dedupes nothing here. Acceptable; we could cache later via a shared signal store if performance demands it.
- **Filter state in URL** — v1 keeps filters in component-scoped signals (not URL params). Deep-linking with filters is a v1.1 polish.

## Acceptance Verification
- [ ] Mockups approved.
- [ ] `/projects` lists projects in the seed dataset; filters narrow the grid.
- [ ] Clicking a card navigates to `/projects/:slug`.
- [ ] `/projects/:slug` renders project header + tabs; "Work items" tab shows the placeholder.
- [ ] 403 and 404 paths render friendly inline messages.
- [ ] Home page top-3 grid replaces the empty placeholder.
- [ ] All new specs pass under `npm run test:ci`.
