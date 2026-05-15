# Implementation Plan: T-012 — Layouts (PublicLayoutComponent and AppShellComponent)

## Task Reference
- **Task ID:** T-012
- **Type:** Frontend
- **Workflow:** mockup-first
- **Complexity:** M
- **Rationale:** Every screen renders inside one of these. Locking the chrome before screen work avoids rework.

## Overview
Build the two layout shells: a centered public layout for unauthenticated screens and the authenticated app shell (header + collapsible sidebar + content outlet). Both expose a `<router-outlet>`.

## Implementation Steps

### Step 0: Mockups
**Files:** `mockups/public-layout.html`, `mockups/app-shell.html`
**Action:** Create
Single-page Tailwind mockups for each layout. App shell mockup covers mobile (drawer closed) and `md:`+ (pinned). Get approval before implementation per `mockup-first`.

### Step 1: PublicLayoutComponent
**Files:** `client/src/app/core/layouts/public-layout/public-layout.component.{ts,html}`
**Action:** Create
```html
<main class="min-h-screen flex items-center justify-center bg-slate-50 p-6">
  <div class="w-full max-w-md">
    <div class="flex justify-center mb-6">
      <span class="text-2xl font-heading font-bold text-slate-900">Portfolio</span>
    </div>
    <router-outlet />
  </div>
</main>
```
Standalone, imports `RouterOutlet`.

### Step 2: HeaderComponent
**Files:** `client/src/app/core/layouts/app-shell/header.component.{ts,html}`
**Action:** Create
Inputs (Signals): `pendingCount: number`, `memberName: string`. Output: `menuToggle` (for mobile hamburger), `logout`.
Renders an `h-14 bg-white border-b border-slate-200 px-4 md:px-6 flex items-center justify-between` with:
- Left: mobile hamburger (visible below `md:`), logo wordmark.
- Center (`md:` and up): a search input slot (`<ng-content select="[header-search]">`), placeholder for global search.
- Right: pending-action badge (`bg-sky-500 text-white rounded-full text-xs h-5 min-w-[20px] px-1 flex items-center justify-center`, hidden when `pendingCount() === 0`) and member menu (display name + dropdown with "Profile" and "Sign out").

### Step 3: SidebarComponent
**Files:** `client/src/app/core/layouts/app-shell/sidebar.component.{ts,html}`
**Action:** Create
Inputs: `open: boolean` (drawer state on mobile), `pendingCount: number`, `currentRoute: string`. Output: `close` (mobile).
Renders `w-64 bg-white border-r border-slate-200 h-full` with nav groups:
1. **Workspace** — Home, Projects.
2. **Pending on you** — placeholder live list (filled in FEAT-005); shows current pending count badge.
3. **Operator** — Operator dashboard, Audit (visible only when operator role; pass an input `isOperator: boolean`).
4. **Admin** — Teams, Members, Executors, Executor Bindings (operator-only).
Nav items: `flex items-center gap-3 rounded-lg px-3 h-10 hover:bg-slate-50 aria-[current=page]:bg-sky-50 aria-[current=page]:text-sky-700`. Use `routerLink` + `routerLinkActive`.

### Step 4: AppShellComponent
**Files:** `client/src/app/core/layouts/app-shell/app-shell.component.{ts,html}`
**Action:** Create
Composes header + sidebar + main content:
```html
<div class="min-h-screen bg-slate-50">
  <app-header [pendingCount]="pending()" [memberName]="member().displayName"
              (menuToggle)="drawer.set(!drawer())" (logout)="onLogout()" />
  <div class="flex">
    <aside class="hidden md:block">
      <app-sidebar [pendingCount]="pending()" [isOperator]="isOperator()" />
    </aside>
    <!-- Mobile drawer -->
    <div *ngIf="drawer()"
         class="fixed inset-0 z-40 bg-slate-900/40 backdrop-blur-sm md:hidden"
         (click)="drawer.set(false)"></div>
    <aside *ngIf="drawer()" class="fixed top-14 left-0 z-50 h-[calc(100vh-3.5rem)] md:hidden">
      <app-sidebar [pendingCount]="pending()" [isOperator]="isOperator()" (close)="drawer.set(false)" />
    </aside>
    <main class="flex-1 min-w-0">
      <div class="mx-auto py-8 px-4 md:px-6 max-w-5xl">
        <router-outlet />
      </div>
    </main>
  </div>
</div>
```
`pending()`, `member()`, `isOperator()` are signals injected from services (placeholder constants until FEAT-005 / FEAT-002).

### Step 5: Specs
**Files:** `*.spec.ts` for `app-shell`, `public-layout`, `sidebar`, `header`
**Action:** Create
- App shell: render outlet, opens drawer on `menuToggle`, hides drawer on `md:`+ at base layout (via responsive class assertion).
- Header: badge hidden when `pendingCount()===0`, visible otherwise.
- Sidebar: operator-only items hidden when `isOperator()===false`.
- Public layout: renders centered card with outlet.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `mockups/public-layout.html` | Create | Stakeholder-approved mockup |
| `mockups/app-shell.html` | Create | Stakeholder-approved mockup |
| `client/src/app/core/layouts/public-layout/*.{ts,html,spec.ts}` | Create | Public layout |
| `client/src/app/core/layouts/app-shell/app-shell.component.{ts,html,spec.ts}` | Create | App shell composition |
| `client/src/app/core/layouts/app-shell/header.component.{ts,html,spec.ts}` | Create | Header |
| `client/src/app/core/layouts/app-shell/sidebar.component.{ts,html,spec.ts}` | Create | Sidebar nav |

## Edge Cases & Risks
- **Drawer lock-up on resize** — if the user opens the drawer at mobile width then resizes to `md:`+, the drawer overlay can remain. Add a host-listener for `window:resize` to close the drawer when `matchMedia('(min-width: 768px)').matches`.
- **Sidebar nav drift over time** — adding new admin screens later means editing this component. Keep nav items defined as an in-component constant array so additions are local.
- **`aria-[current=page]` styling** — Tailwind 4 supports `aria-*` variants; verify by inspecting the active item in the rendered output.

## Acceptance Verification
- [ ] Both mockups approved before implementation.
- [ ] `PublicLayoutComponent` renders a centered `max-w-md` card on `bg-slate-50` with logo above the outlet.
- [ ] `AppShellComponent` shows the sidebar from `md:` and up; on mobile, the drawer opens via hamburger.
- [ ] Header pending-action badge hides at `pendingCount===0` and shows the count otherwise.
- [ ] Sidebar exposes Home, Projects, Pending on you, Operator, Admin groups; operator-only items hidden when `isOperator===false`.
- [ ] Specs pass under `ng test`.
