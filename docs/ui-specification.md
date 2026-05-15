# UI Specification

## Overview

The Portfolio SPA is the **single front door** for every end user. It surfaces every project the member belongs to, every work item in those projects, and every checkpoint waiting on the member's role — without the member ever knowing which lifecycle executor handled what. It also hosts the operator dashboard (cross-project view of in-flight work and pending approvals) and the admin surfaces for workspace primitives and the executor registry.

The visual identity is **Modern Minimal** (compiled from the `modern-minimal` DDR profile): sky-blue primary, Poppins headings, Inter body, generous whitespace, elevated cards on a soft slate background, calm content-first rhythm.

Roughly 14 first-class screens grouped into five areas:

1. **Auth** — Login.
2. **Home** — Pending-action inbox + project picker.
3. **Project** — Project home (work-item list), work-item review, lifecycle-aware review screen.
4. **Operator** — Cross-project dashboard, audit log.
5. **Admin** — Teams, Members, Project memberships, Executor registry.

### Key UI Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Component library | None pre-built (no Angular Material). Hand-rolled standalone components styled with Tailwind utilities | Matches the `tailwind-no-css` ADR and `modern-minimal` DDR profile; design control end-to-end |
| Layout pattern | Persistent left sidebar (collapsible on mobile) + top header + main content area | One-shell-for-everything; Stakeholder rule "single front door" |
| Styling approach | Tailwind CSS 4+ utility classes only; no component CSS files; no inline styles | Profile: `tailwind-no-css`; consistency, rapid iteration |
| Responsive strategy | **Mobile-first** — base styles assume mobile; `sm:`, `md:`, `lg:`, `xl:` layer up | `modern-minimal` profile (excludes desktop-first DDR) |
| State management | Angular Signals for local + cross-component state; RxJS only for HTTP and SSE streams | Profile: `signals-state` |
| Live state | SSE consumed via `EventSource`; rendered as it arrives; no buffering | Stakeholder rule 4 — pass-through |
| Templates | Separate `.html` files via `templateUrl` | Profile: `separate-template-file` |
| Components | All **standalone**; no `NgModules` | Profile: `standalone-components` |
| Routing | Lazy-loaded feature routes; route guards check (membership, role) before activation | Authorization-first applies in the UI too |
| Notification surface | Persistent "Pending on you" inbox in the sidebar + live SSE updates from `/api/notifications/stream` | Stakeholder: "discovers pending action without polling" |
| Accessibility floor | WCAG AA contrast, visible focus rings on every interactive element, `aria-live` for stream announcements | Profile: `accessibility/*` |

## Design System

> Compiled from the `modern-minimal` DDR profile. Project-specific deltas, if any, would be called out here — there are none for v1.

### Brand Colors

| Token | Hex | Tailwind | Usage |
|-------|-----|----------|-------|
| `primary` | #0EA5E9 | `sky-500` | Primary actions, active states, links |
| `primary-light` | #E0F2FE | `sky-100` | Hover backgrounds, selected states |
| `primary-dark` | #0284C7 | `sky-600` | Active/pressed states |
| `on-primary` | #FFFFFF | `white` | Text on primary backgrounds |
| `secondary` | #8B5CF6 | `violet-500` | Secondary actions, accents |
| `on-secondary` | #FFFFFF | `white` | Text on secondary backgrounds |
| `neutral-50` | #F8FAFC | `slate-50` | Page background |
| `neutral-100` | #F1F5F9 | `slate-100` | Subtle section backgrounds |
| `neutral-200` | #E2E8F0 | `slate-200` | Borders, dividers |
| `neutral-300` | #CBD5E1 | `slate-300` | Input borders |
| `neutral-500` | #64748B | `slate-500` | Secondary text |
| `neutral-700` | #334155 | `slate-700` | Body text |
| `neutral-900` | #0F172A | `slate-900` | Headings |
| `success` | #10B981 | `emerald-500` | Success states (executor success, granted audit) |
| `warning` | #F59E0B | `amber-500` | Warning states (paused executor, pending action) |
| `error` | #EF4444 | `red-500` | Error states (denied audit, executor failure) |
| `info` | #0EA5E9 | `sky-500` | Info states (same as primary) |

### Typography Scale

| Level | Font | Size | Weight | Line height | Usage |
|-------|------|------|--------|-------------|-------|
| `h1` | Poppins | 2.25rem (36px) | 700 | 1.2 | Page titles |
| `h2` | Poppins | 1.75rem (28px) | 600 | 1.3 | Section headings |
| `h3` | Poppins | 1.25rem (20px) | 600 | 1.4 | Card / panel titles |
| `body` | Inter | 1rem (16px) | 400 | 1.6 | Body text |
| `body-sm` | Inter | 0.875rem (14px) | 400 | 1.5 | Secondary text, metadata |
| `caption` | Inter | 0.75rem (12px) | 400 | 1.4 | Timestamps, hints |

### Spacing Scale

| Token | Value | Usage |
|-------|-------|-------|
| `space-2` | 0.5rem (8px) | Tight internal spacing (icon + label) |
| `space-4` | 1rem (16px) | Default form-field gap, inline element spacing |
| `space-6` | 1.5rem (24px) | Card internal padding (`p-6`) |
| `space-8` | 2rem (32px) | Section vertical rhythm (`gap-8`), page vertical padding (`py-8`) |
| `space-12` | 3rem (48px) | Major section separators |

### Component Library (hand-rolled, Tailwind-styled)

| UI Need | Component | Notes |
|---------|-----------|-------|
| Primary button | `<app-button variant="primary">` | `bg-sky-500 hover:bg-sky-600 text-white rounded-lg px-4 py-2 font-medium transition` |
| Secondary button | `<app-button variant="secondary">` | `bg-white border border-slate-300 hover:bg-slate-50 text-slate-700 rounded-lg` |
| Ghost button | `<app-button variant="ghost">` | `text-sky-600 hover:bg-sky-50 rounded-lg` |
| Danger button | `<app-button variant="danger">` | `bg-red-500 hover:bg-red-600 text-white rounded-lg` |
| Card | `<app-card>` | `bg-white rounded-xl shadow-sm p-6` (clickable: `hover:shadow-md hover:-translate-y-0.5 transition-all duration-200`) |
| Form field | `<app-form-field>` | Label + input + helper + error slot; `border border-slate-300 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 rounded-lg` |
| Table | `<app-table>` | Header `bg-slate-50 text-slate-500 uppercase text-xs`; rows `border-b border-slate-200`; hover `bg-slate-50` |
| Modal | `<app-modal>` | Centered card on `bg-slate-900/40 backdrop-blur-sm` overlay; `rounded-xl shadow-lg max-w-lg` |
| Sidebar | `<app-sidebar>` | Fixed left rail, `w-64`, `bg-white border-r border-slate-200`; collapses to a drawer below `md:` |
| Top header | `<app-header>` | `h-14 bg-white border-b border-slate-200 px-6 flex items-center justify-between` |
| Badge / Chip | `<app-badge variant="...">` | Status pills: `bg-{semantic}-100 text-{semantic}-700 rounded-full px-2 py-0.5 text-xs` |
| Empty state | `<app-empty-state>` | Centered illustration + heading + description + CTA |
| Error banner | `<app-error-banner>` | `bg-red-50 border border-red-200 text-red-700 rounded-lg p-4 flex items-start gap-3` with retry slot |
| Skeleton | `<app-skeleton>` | `bg-slate-200 animate-pulse rounded` — sized to match real content |
| Spinner | `<app-spinner>` | Inline SVG, `text-sky-500`, used for action buttons + small in-flight indicators |
| Toast | `<app-toast>` | Top-right; auto-dismiss; `aria-live="polite"` |
| Stream feed | `<app-stream-feed>` | Renders SSE events as they arrive; never buffers; `aria-live="polite"` for new entries |

### State Patterns

| State | Pattern | Key Constraints | Where used |
|-------|---------|----------------|------------|
| Loading (skeleton) | Skeleton placeholders with `animate-pulse`, sized to match real content | No spinners for initial page loads | Project list, work-item list, work-item detail header |
| Loading (action) | Inline `<app-spinner>` in the action button; button disabled | Use only for explicit user actions, never page loads | "Approve" button on checkpoint signal |
| Empty | `<app-empty-state>` with heading + 1-line description + primary CTA where appropriate | Always include heading + (when applicable) CTA | "No projects yet", "No work items", "Nothing waiting on you" |
| Error | `<app-error-banner>` with human-readable title + RFC 7807 `detail` + retry button | Never render raw error bodies; surface only `title` + `detail` + `correlationId` | Any failing API call |
| Forbidden (403) | Inline banner: "You don't have the required role on this project." with link to project page | Never reveal what role is missing if the member doesn't belong to the project at all | Direct navigation to a non-member project; checkpoint signal denial |
| Executor failure (502) | Banner: "The lifecycle executor refused this action." with `correlationId` and a copy button | Help operators diagnose without exposing executor internals to end users | Start work item, signal checkpoint |
| Live stream connecting | Subtle "Connecting live updates…" caption above the stream feed; replaced by feed on first event | Never block the page on stream connection | Lifecycle review screen, work-item detail |
| Stream disconnected | Inline caption + reconnect button; existing rendered events stay on screen | Don't clear history on disconnect | Same |

### Responsive Breakpoints

**Strategy:** **Mobile-first.** Base styles assume mobile; layer up.

| Breakpoint | Width | Tailwind | Primary use |
|------------|-------|----------|-------------|
| Mobile | < 640px | (base) | Single column, drawer sidebar (closed by default), full-width cards |
| Small | 640–767px | `sm:` | Slight density increase; cards may sit side-by-side in 2 columns where it helps |
| Tablet | 768–1023px | `md:` | Sidebar pinned visible; 2-column list/detail where space allows |
| Desktop | 1024–1279px | `lg:` | Full app shell; reading widths capped at `max-w-5xl` |
| Wide | ≥ 1280px | `xl:` | Dashboards may use `max-w-7xl`; review screens still cap at `max-w-5xl` |

**Reading-focused page widths** (work-item detail, lifecycle review screens) cap at `max-w-5xl`. **Dashboards** (operator dashboard, work-item lists) may use `max-w-7xl`.

## Screen Inventory

| # | Screen Name | Route | Auth Required | Parent Layout | Primary User Action |
|---|-------------|-------|---------------|---------------|---------------------|
| 1 | Login | `/login` | No | Public | Authenticate |
| 2 | Home (Pending on you) | `/` | Yes | App shell | Pick a pending checkpoint or jump to a project |
| 3 | Project list | `/projects` | Yes | App shell | Open a project the member belongs to |
| 4 | Project home | `/projects/:slug` | Yes (Project:any) | App shell | Browse work items, start a new one |
| 5 | Work-item detail | `/projects/:slug/work-items/:id` | Yes (Project:any) | App shell | Read state, open lifecycle review, watch live stream |
| 6 | Lifecycle review (feature-delivery) | `/projects/:slug/work-items/:id/review` | Yes (Project:any) | App shell | Read the artefact, signal a checkpoint |
| 7 | Operator dashboard | `/operator` | Yes (System:operator) | App shell | Scan cross-project in-flight + pending approvals |
| 8 | Audit log | `/projects/:slug/audit` and `/operator/audit` | Yes (Project:any / System:operator) | App shell | Inspect audit entries |
| 9 | Teams | `/admin/teams` | Yes (System:operator) | App shell | Manage teams |
| 10 | Members | `/admin/members` | Yes (System:operator) | App shell | Manage members |
| 11 | Project memberships | `/projects/:slug/admin/memberships` | Yes (System:operator) | App shell | Manage project members + role assignments |
| 12 | Executors | `/admin/executors` | Yes (System:operator) | App shell | Register / inspect lifecycle executors |
| 13 | Executor bindings | `/admin/executor-bindings` | Yes (System:operator) | App shell | Bind project types to executors |
| 14 | Profile | `/me` | Yes (Authenticated) | App shell | View own profile, logout |

## Shared Layouts

### App Shell (Authenticated)

```
┌─────────────────────────────────────────────────────────────────────┐
│ Header   [Portfolio logo]      [global search]    [pending: 3] [me] │
├──────────┬──────────────────────────────────────────────────────────┤
│ Sidebar  │  Main content area                                       │
│          │  ┌──────────────────────────────────────────────────┐    │
│ Home     │  │                                                  │    │
│ Projects │  │   [Page content via router]                      │    │
│ ───────  │  │                                                  │    │
│ Pending  │  │   max-w-5xl for review pages                     │    │
│  on you  │  │   max-w-7xl for dashboards                       │    │
│ (live)   │  │                                                  │    │
│ ───────  │  │                                                  │    │
│ Operator │  │                                                  │    │
│ Admin    │  └──────────────────────────────────────────────────┘    │
└──────────┴──────────────────────────────────────────────────────────┘
```

- Header is `h-14`, white, bottom border `slate-200`. Right side shows a pending-action count badge (sky-500 when > 0) and the member menu.
- Sidebar is `w-64` from `md:` up; below `md:` it collapses behind a hamburger.
- Background is `bg-slate-50`; content cards sit on white.
- The "Pending on you" sidebar group is live — it subscribes to `/api/notifications/stream`.

### Public Layout (Unauthenticated)

```
┌──────────────────────────────────────────────────────────┐
│                                                          │
│                                                          │
│              ┌──────────────────────────┐                │
│              │   [Portfolio logo]       │                │
│              │   Sign in                │                │
│              │   [email]                │                │
│              │   [password]             │                │
│              │   [Sign in →]            │                │
│              └──────────────────────────┘                │
│                                                          │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

Centered card on `bg-slate-50`. `max-w-md`, `shadow-sm`, `rounded-xl`.

## Screen Specifications

### 1. Login

**Route:** `/login` · **Auth:** Public · **Layout:** Public

```
┌─────────────────────────────────┐
│         [Portfolio]             │
│   Sign in to your workspace     │
│                                 │
│   Email      [_______________]  │
│   Password   [_______________]  │
│                                 │
│   [   Sign in   →   ]           │
│                                 │
│   (error banner here on 401)    │
└─────────────────────────────────┘
```

**Component Hierarchy**

```
LoginPage
├── AppCard
│   ├── h1 ("Sign in")
│   ├── LoginForm (standalone)
│   │   ├── AppFormField (email)
│   │   ├── AppFormField (password)
│   │   └── AppButton (primary, "Sign in")
│   └── AppErrorBanner (on auth failure)
```

**Component → API**

| Component | Data | API | Trigger |
|-----------|------|-----|---------|
| LoginForm | — | `POST /api/auth/login` | Submit |

**States**

| State | Condition | UI |
|-------|-----------|----|
| Default | Idle | Form |
| Loading | Submit in flight | Spinner in button; fields disabled |
| Error | 400/401/403 | Inline error banner with `detail` |

**Interactions**

| Action | Element | Result | API |
|--------|---------|--------|-----|
| Submit | "Sign in" button or Enter | Stores access token in memory; refresh cookie set by server; redirects to `/` | `POST /api/auth/login` |

### 2. Home (Pending on you)

**Route:** `/` · **Auth:** Authenticated · **Layout:** App shell

This is the **first screen** after login. It answers "what is waiting on me, anywhere?" — the persona's core problem.

```
┌──────────────────────────────────────────────────────────┐
│ Welcome back, {displayName}                              │
│                                                          │
│ Pending on you                                           │
│ ┌────────────────────────────────────────────────────┐   │
│ │ [Project · Feature delivery]                       │   │
│ │ Review the implementation for "Add CSV export"     │   │
│ │ Checkpoint: implementation-review · raised 2h ago  │   │
│ │ [  Open review  →  ]                               │   │
│ └────────────────────────────────────────────────────┘   │
│ ┌────────────────────────────────────────────────────┐   │
│ │ ...                                                │   │
│ └────────────────────────────────────────────────────┘   │
│                                                          │
│ Your projects                                            │
│ ┌───────────┬───────────┬───────────┐                    │
│ │ ProjectA  │ ProjectB  │ ProjectC  │                    │
│ │ 3 in-flt  │ 1 waiting │ 0 in-flt  │                    │
│ └───────────┴───────────┴───────────┘                    │
└──────────────────────────────────────────────────────────┘
```

**Component Hierarchy**

```
HomePage
├── PendingActionList
│   ├── PendingActionCard (per item)
│   │   └── AppButton ("Open review →")
│   └── EmptyState ("Nothing waiting on you.")
└── ProjectGrid
    └── ProjectCard (per item)
```

**Component → API**

| Component | Data | API | Trigger |
|-----------|------|-----|---------|
| PendingActionList | initial list | `GET /api/notifications/pending` | Page load |
| PendingActionList | live updates | `GET /api/notifications/stream` (SSE) | After initial load |
| ProjectGrid | projects | `GET /api/projects?sortBy=updatedAt&sortDir=desc` | Page load |

**States**

| State | Condition | UI |
|-------|-----------|----|
| Default | Lists loaded | Render |
| Loading | Initial fetch | Skeletons sized to cards |
| Empty (pending) | No pending actions | `<EmptyState>` "You're all caught up." |
| Empty (projects) | No memberships | `<EmptyState>` "An operator hasn't added you to any project yet." |
| Stream disconnected | SSE down | Inline caption + reconnect button; existing entries remain |
| Error | Either fetch fails | Inline error banner per section with retry |

**Interactions**

| Action | Element | Result | API |
|--------|---------|--------|-----|
| Open review | "Open review →" button | Navigate to `/projects/:slug/work-items/:id/review` | — |
| Open project | Project card | Navigate to `/projects/:slug` | — |
| Live update | (SSE event) | New `PendingActionCard` slides in at top with `aria-live="polite"` announcement | — |

### 3. Project list

**Route:** `/projects` · **Auth:** Authenticated · **Layout:** App shell

Card grid of projects the caller can see, with filter chips by team and project type.

| Component | Data | API | Trigger |
|-----------|------|-----|---------|
| ProjectGrid | projects | `GET /api/projects` (paginated, filters: `teamId`, `projectType`, `status`) | Page load + filter change |

States: Default / Loading (skeleton cards) / Empty / Error.

### 4. Project home

**Route:** `/projects/:slug` · **Auth:** Project:any · **Layout:** App shell

```
┌──────────────────────────────────────────────────────────┐
│ {Project name}                          [Start work →]   │
│ Team · {team name} · projectType · {type}                │
├──────────────────────────────────────────────────────────┤
│ Filters: [Status ▾] [Waiting on me ☑] [Search...]        │
├──────────────────────────────────────────────────────────┤
│ Title                Status         Waiting on  Updated  │
│ Add CSV export       WaitingOnCkpt  Reviewer    2h ago   │
│ Migrate to v2 API    Running          —          5m ago  │
│ ...                                                      │
│ [page 1 of 3]                                            │
└──────────────────────────────────────────────────────────┘
```

**Component Hierarchy**

```
ProjectHomePage
├── ProjectHeader (name, team, projectType, actions)
├── WorkItemFilters (status, waitingOnMe, search)
├── WorkItemTable
│   ├── AppTable
│   └── PaginationBar
└── StartWorkModal (lazy)
```

**Component → API**

| Component | Data | API | Trigger |
|-----------|------|-----|---------|
| ProjectHeader | project | `GET /api/projects/{id}` | Page load |
| WorkItemTable | items | `GET /api/projects/{id}/work-items` (paginated, filters) | Page load + filter change |
| StartWorkModal | (input shape — opaque to portfolio) | `POST /api/projects/{id}/work-items` | Submit |

**States** per the global pattern. Additionally: 403 if the route guard finds the member is not on the project — redirects to `/projects` with a toast.

**Interactions**

| Action | Element | Result | API |
|--------|---------|--------|-----|
| Start work | "Start work →" button | Opens StartWorkModal; allowed only if the caller has the start-role | `POST /api/projects/{id}/work-items` on submit |
| Open item | Row click | Navigate to `/projects/:slug/work-items/:id` | — |
| Filter "Waiting on me" | Toggle | Refetch with `waitingOnMe=true` | `GET /api/projects/{id}/work-items?waitingOnMe=true` |

### 5. Work-item detail

**Route:** `/projects/:slug/work-items/:id` · **Auth:** Project:any · **Layout:** App shell

Generic, executor-agnostic surface. Renders title, status, executor metadata, signal history, and the live stream feed. Provides a button "Open review" that routes to the lifecycle-aware review screen when the executor + project type matches a known shape (v1: feature-delivery).

**Component Hierarchy**

```
WorkItemDetailPage
├── WorkItemHeader (title, status badge, executor chip, "Open review" CTA)
├── ExecutorStatePanel        # renders the opaque executorState as a key-value list
├── StreamFeed                # SSE pass-through, aria-live="polite"
└── SignalHistoryList         # most-recent 20 signals + "Load more"
```

| Component | Data | API | Trigger |
|-----------|------|-----|---------|
| WorkItemHeader / ExecutorStatePanel | work item | `GET /api/projects/{id}/work-items/{wid}` | Page load |
| StreamFeed | stream events | `GET /api/projects/{id}/work-items/{wid}/stream` (SSE) | After initial fetch |
| SignalHistoryList | signals | `GET /api/projects/{id}/work-items/{wid}/signals` | Page load |

**Interactions**

| Action | Element | Result | API |
|--------|---------|--------|-----|
| Open review | "Open review →" | Navigate to the lifecycle-aware review screen | — |
| Cancel | "Cancel" button (role-gated) | Confirms then forwards cancel | `POST /api/projects/{id}/work-items/{wid}/cancel` |

### 6. Lifecycle review (feature-delivery) — the lifecycle-aware screen

**Route:** `/projects/:slug/work-items/:id/review` · **Auth:** Project:any · **Layout:** App shell (max-w-5xl content)

> The required "at least one lifecycle-aware screen" per the Stakeholder Definition. Demonstrates the rendering pattern against feature-delivery: brief → tasks → plan → implementation timeline, plus the active checkpoint actions.

```
┌──────────────────────────────────────────────────────────┐
│ {Work item title}        [executor: feature-delivery]    │
│ Status: WaitingOnCheckpoint · implementation-review      │
├──────────────────────────────────────────────────────────┤
│ Timeline                                                 │
│  ● Brief         (approved 2d ago by Ana)                │
│  ● Tasks         (approved 1d ago by Ana)                │
│  ● Plan          (approved 5h ago by Beto)               │
│  ● Implementation (in review — you)        ◀ active      │
│                                                          │
├──────────────────────────────────────────────────────────┤
│ Artefact (active step)                                   │
│ [Diff panel / file tree / rendered markdown]             │
│                                                          │
├──────────────────────────────────────────────────────────┤
│ Decision history                                         │
│  Plan approved by Beto · 5h ago "Looks right"            │
│  Tasks approved by Ana · 1d ago                          │
│  ...                                                     │
├──────────────────────────────────────────────────────────┤
│ Your action (role: reviewer)                             │
│  [ Approve ✓ ]  [ Send back ✗ ]  [ Revise ↺ ]            │
│  Notes: [_____________________________________]          │
├──────────────────────────────────────────────────────────┤
│ Live trace                                               │
│  [streaming feed of executor events]                     │
└──────────────────────────────────────────────────────────┘
```

**Component Hierarchy**

```
LifecycleReviewPage
├── ReviewHeader                # title, status, executor chip
├── LifecycleTimeline           # ordered list of steps with state (approved / active / pending)
├── ActiveStepArtefactPanel     # renders the artefact for the currently active checkpoint
│   ├── DiffViewer (when artefact is a diff)
│   ├── MarkdownRenderer (when artefact is a doc)
│   └── ArtefactFallback (key-value, when neither)
├── DecisionHistoryList         # one entry per past signal
├── CheckpointActionBar         # role-gated buttons + payload editor
│   └── AppButton (per allowedOutcome)
└── StreamFeed                  # SSE
```

**Component → API**

| Component | Data | API | Trigger |
|-----------|------|-----|---------|
| ReviewHeader, ActiveStepArtefactPanel | work item + executor state | `GET /api/projects/{id}/work-items/{wid}` | Page load |
| LifecycleTimeline | derived from `executorState.steps` + `signals` | (same call) | Page load |
| DecisionHistoryList | signals | `GET /api/projects/{id}/work-items/{wid}/signals` | Page load |
| CheckpointActionBar | contract | `GET /api/projects/{id}/work-items/{wid}/checkpoints/{key}` | Page load |
| CheckpointActionBar | submit | `POST /api/projects/{id}/work-items/{wid}/checkpoints/{key}/signal` | User click |
| StreamFeed | events | `GET /api/projects/{id}/work-items/{wid}/stream` (SSE) | After initial fetch |

**States**

| State | Condition | UI |
|-------|-----------|----|
| Default | Loaded, member holds required role for active checkpoint | Action bar enabled |
| Read-only | Loaded but member lacks required role | Action bar disabled with caption "This step is waiting on role: approver." |
| No active checkpoint | Status ∈ {`Running`, `Completed`, `Cancelled`} | Action bar hidden; banner reflects status |
| Loading | Initial fetch | Skeletons for header, timeline, artefact |
| Error | Any fetch fails | Inline banner per panel with retry |
| Submitting | Signal in flight | Spinner in chosen outcome button; bar disabled |
| Submit failure | 4xx/5xx | RFC-7807-rendered banner above the bar; bar re-enabled |

**Interactions**

| Action | Element | Result | API |
|--------|---------|--------|-----|
| Choose outcome | One of the role-gated buttons (`Approve`, `Send back`, `Revise`) | Submits the signal | `POST /api/projects/{id}/work-items/{wid}/checkpoints/{key}/signal` |
| Stream event | (SSE) | Appended to StreamFeed; if event indicates the checkpoint resolved, the page refetches the work item and routes to the next active step | — |
| Open a past artefact | Click a past step in the timeline | Switches the ArtefactPanel to read-only of that step's artefact | — |

### 7. Operator dashboard

**Route:** `/operator` · **Auth:** System:operator · **Layout:** App shell (max-w-7xl)

Cross-project view: total in-flight work, pending approvals grouped by project, recent failures (audit `Failed`), recent denies. The "routing-layer replacement."

| Panel | API |
|-------|-----|
| In-flight totals | derived from `GET /api/projects` + `GET /api/projects/{id}/work-items?status=Running,WaitingOnCheckpoint` (batched per project) |
| Pending approvals (all projects) | aggregated client-side from per-project queries (v1) — could be a dedicated endpoint in v2 |
| Recent audit events | `GET /api/admin/audit?outcome=Denied,Failed&sortBy=occurredAt&sortDir=desc` |

### 8. Audit log

**Route:** `/projects/:slug/audit` (project) and `/operator/audit` (cross-project).

Filterable table per the audit DTO. Cells: occurredAt · actingMember · target · action · outcome (color-coded badge) · reason. Click a row to expand `details_json`.

### 9–13. Admin screens

Standard CRUD tables with row actions. Each follows the same shape:

- **Top of page:** title, description, `[New …]` primary button.
- **Body:** `<app-table>` with filters above and pagination below.
- **Row actions:** Edit (opens modal) and Delete (confirmation modal, soft delete).
- **All writes:** RFC 7807 errors rendered as banners over the modal; success toasts on close.

Operator-only (route guard).

**Project memberships** (`/projects/:slug/admin/memberships`) is reachable from inside a project as well as from the operator surface; it is the place where a member is added to a project and given role assignments.

### 14. Profile

**Route:** `/me` · **Auth:** Authenticated · **Layout:** App shell

Read-only view of the current member's profile + a "Sign out" button (`POST /api/auth/logout` then redirect to `/login`).

## Shared Components

### AppCard

**Used in:** every screen.
**Description:** Elevated content container. No border. `bg-white rounded-xl shadow-sm p-6`. Clickable variant adds `hover:shadow-md hover:-translate-y-0.5 transition-all duration-200` and a focus ring.

| Name | Direction | Type | Description |
|------|-----------|------|-------------|
| clickable | Input | boolean | If true, applies hover lift and focus ring |
| clicked | Output | EventEmitter\<void\> | Emitted when the card is activated (click / Enter / Space) |

### AppButton

**Used in:** every screen.

| Name | Direction | Type | Description |
|------|-----------|------|-------------|
| variant | Input | `'primary' \| 'secondary' \| 'ghost' \| 'danger'` | Visual style |
| size | Input | `'sm' \| 'md' \| 'lg'` (default `'md'`) | |
| disabled | Input | boolean | |
| loading | Input | boolean | Shows inline spinner; disables click |
| iconLeft / iconRight | Input | TemplateRef | Optional icon slots |
| clicked | Output | EventEmitter\<MouseEvent\> | |

### AppFormField

**Used in:** Login, all admin modals, Start-work modal, Checkpoint action bar (notes).

| Name | Direction | Type | Description |
|------|-----------|------|-------------|
| label | Input | string | |
| helperText | Input | string | |
| error | Input | string | Renders below the input in red; sets `aria-invalid` |
| required | Input | boolean | |

### StreamFeed

**Used in:** Work-item detail, Lifecycle review.

| Name | Direction | Type | Description |
|------|-----------|------|-------------|
| streamUrl | Input | string | SSE URL relative to `/api/` |
| event | Output | EventEmitter\<StreamEvent\> | Emitted as events arrive (also rendered inline) |
| connectionState | Output | Signal\<`'connecting' \| 'open' \| 'closed'`\> | For surrounding UI to react |

### PendingActionCard

**Used in:** Home, sidebar live list, Operator dashboard.

| Name | Direction | Type | Description |
|------|-----------|------|-------------|
| signal | Input | PendingActionDto | The pending action |
| opened | Output | EventEmitter\<PendingActionDto\> | Emitted when "Open review →" is clicked |

### CheckpointActionBar

**Used in:** Lifecycle review.

| Name | Direction | Type | Description |
|------|-----------|------|-------------|
| contract | Input | CheckpointContractDto | Drives the buttons and outcomes |
| memberHasRole | Input | boolean | Disables the bar with explanation when false |
| submit | Output | EventEmitter\<{ outcome: string; payload?: object }\> | Submitted action |

### LifecycleTimeline

**Used in:** Lifecycle review (feature-delivery). Generic enough to render any executor that exposes `steps` in `executorState`.

| Name | Direction | Type | Description |
|------|-----------|------|-------------|
| steps | Input | `Array<{ key: string; displayName: string; state: 'approved' \| 'active' \| 'pending' \| 'rejected'; resolvedBy?: { id; displayName }; resolvedAt?: string }>` | |
| activeStepKey | Input | string \| null | |
| stepSelected | Output | EventEmitter\<string\> | When the user clicks a past step to inspect its artefact |

### AppTable / PaginationBar

Used in every list screen. Inputs: column defs (header, cell renderer, sortable), rows, pagination meta. Outputs: row click, sort change, page change.

## AI Task Generation Notes

- **Derive component structure** from the Component Hierarchy for each screen — do not invent extra components.
- **Map data requirements** from Component → API Mapping. Every API call cited here exists in `docs/api-spec.md`.
- **Specify all states** — every component must handle loading, empty, error, and (where applicable) forbidden / executor-failure states.
- **Define interactions precisely** — each maps to a UI element, result, and API call.
- **Reuse shared components** — `AppCard`, `AppButton`, `AppFormField`, `StreamFeed`, `PendingActionCard`, `CheckpointActionBar`, `LifecycleTimeline`, `AppTable` are the building blocks. Do not duplicate.
- **Follow the Modern Minimal design system** for colors, typography, and spacing. Cards are elevated (`shadow-sm`, no border). Buttons are `rounded-lg`. Page padding `py-8`, card padding `p-6`, section gaps `gap-8`.
- **Mobile-first.** Every screen is designed at mobile width first; `md:`/`lg:` add density.
- **Streaming is pass-through.** `StreamFeed` must not buffer or transform events. Render as they arrive.
- **Authorization in the UI is defense in depth, not security.** Route guards check `(membership, role)` from `GET /api/auth/me`; the **server** is the authoritative gate on every action.
- **Never render raw executor errors.** Use the RFC 7807 `title` + `detail` + `correlationId`.

## Changelog

- **2026-05-15** — Initial UI specification. Defines the app shell, 14 screens (auth, home, project, lifecycle review, operator, audit, 5 admin, profile), the Modern Minimal design system compiled from the DDR profile, and shared components (`AppCard`, `AppButton`, `StreamFeed`, `CheckpointActionBar`, `LifecycleTimeline`).
