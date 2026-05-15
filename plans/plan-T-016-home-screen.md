# Implementation Plan: T-016 — Home screen (placeholder with empty state)

## Task Reference
- **Task ID:** T-016
- **Type:** Frontend
- **Workflow:** mockup-first
- **Complexity:** S
- **Rationale:** FEAT-001 AC-3 lands here. Demonstrates the App shell + foundational components end-to-end.

## Overview
A welcome heading using the resolved member's display name, plus two `EmptyState` placeholders (Pending on you, Your projects) that later FEATs replace with live lists and grids.

## Implementation Steps

### Step 0: Mockup
**File:** `mockups/home-empty.html`
**Action:** Create
Tailwind mockup of the empty Home as described in `docs/ui-specification.md` § Home. Approve before code.

### Step 1: HomePage component
**Files:** `client/src/app/features/home/home.page.{ts,html,spec.ts}`
**Action:** Create
```ts
@Component({
  selector: 'app-home-page',
  standalone: true,
  imports: [EmptyState],
  templateUrl: './home.page.html',
})
export class HomePage {
  private readonly auth = inject(AuthService);
  readonly displayName = computed(() => this.auth.currentMember()?.displayName ?? '');
}
```
Template:
```html
<header class="mb-8">
  <h1 class="text-3xl font-heading font-bold text-slate-900">Welcome back, {{ displayName() }}</h1>
</header>

<section class="mb-12">
  <h2 class="text-xl font-heading font-semibold mb-4">Pending on you</h2>
  <empty-state
    title="You're all caught up."
    description="When a checkpoint waits on your role in any project, it shows up here." />
</section>

<section>
  <h2 class="text-xl font-heading font-semibold mb-4">Your projects</h2>
  <empty-state
    title="No projects yet."
    description="An operator hasn't added you to any project yet." />
</section>
```

### Step 2: Specs
**File:** `client/src/app/features/home/home.page.spec.ts`
**Action:** Create
- Renders the resolved member's display name in the welcome heading.
- Renders the two `empty-state` placeholders with the documented titles.
- Makes no HTTP calls beyond bootstrap (verify via `HttpTestingController.verify()` with `expectNone(...)`).

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `mockups/home-empty.html` | Create | Stakeholder-approved mockup |
| `client/src/app/features/home/home.page.{ts,html,spec.ts}` | Create | Home page |

## Edge Cases & Risks
- **`displayName()` empty on first render** — `auth.currentMember()` is non-null after the `provideAppInitializer` resolves (T-013); guards prevent reaching `/` before that. If somehow null, render an empty string (the welcome heading will read "Welcome back, " — acceptable for v1).
- **Sidebar's "Pending on you" group also stays empty** — confirmed; the live group lands in FEAT-005.
- **Layout shift on first paint** — both empty states have fixed-ish heights via `py-12`, so the layout is stable.

## Acceptance Verification
- [ ] Mockup approved before implementation.
- [ ] After login as the seed operator, navigating to `/` renders the welcome heading with `Welcome back, <displayName>`.
- [ ] Both sections show the documented empty states.
- [ ] Spec passes; no HTTP calls beyond bootstrap.
