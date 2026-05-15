# Implementation Plan: T-011 — Foundational standalone components

## Task Reference
- **Task ID:** T-011
- **Type:** Frontend
- **Workflow:** mockup-first
- **Complexity:** L
- **Rationale:** Every screen reuses these. Building them up-front avoids inconsistent re-implementations in later screen tasks.

## Overview
Implement six standalone components — AppCard, AppButton, AppFormField, AppErrorBanner, AppSpinner, EmptyState — exactly as specified in `docs/ui-specification.md` § Shared Components. Mockup first, then implementation. Templates in separate `.html`, no `.css` files, Tailwind utilities only.

## Implementation Steps

### Step 0: Mockup
**File:** `mockups/foundational-components.html`
**Action:** Create
Single page rendering each component in each variant + key states (idle / hover / focus / disabled / loading / error). Use plain HTML + Tailwind CDN to keep the mockup self-contained. Hand off for stakeholder approval before code; per CLAUDE.md routing.

### Step 1: AppSpinner (no dependencies, used by AppButton)
**Files:** `client/src/app/shared/components/app-spinner/app-spinner.component.{ts,html}`
**Action:** Create
Inline SVG circle with `animate-spin`. Size input: `'sm' | 'md' | 'lg'` mapping to `h-4 w-4`, `h-5 w-5`, `h-6 w-6`. Color via `currentColor` so consumers control via `text-sky-500` etc.

### Step 2: AppButton
**Files:** `client/src/app/shared/components/app-button/app-button.component.{ts,html}`
**Action:** Create
Inputs (Signals): `variant: 'primary'|'secondary'|'ghost'|'danger'` (default `primary`), `size: 'sm'|'md'|'lg'` (default `md`), `disabled: boolean`, `loading: boolean`, `type: 'button'|'submit'` (default `button`). Output: `clicked: EventEmitter<MouseEvent>`.
Class table (computed via `computed()`):
- primary: `bg-sky-500 hover:bg-sky-600 active:bg-sky-700 text-white`
- secondary: `bg-white border border-slate-300 hover:bg-slate-50 text-slate-700`
- ghost: `text-sky-600 hover:bg-sky-50`
- danger: `bg-red-500 hover:bg-red-600 text-white`
Base: `inline-flex items-center justify-center gap-2 rounded-lg font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-300 disabled:opacity-50 disabled:cursor-not-allowed`.
Size: `text-sm h-9 px-3` / `text-base h-10 px-4` / `text-lg h-12 px-5`.
When `loading`, render `<app-spinner size="sm" />` and disable the click.

### Step 3: AppCard
**Files:** `client/src/app/shared/components/app-card/app-card.component.{ts,html}`
**Action:** Create
Input: `clickable: boolean`. Output: `clicked`. Base: `bg-white rounded-xl shadow-sm p-6`. Clickable: add `cursor-pointer hover:shadow-md hover:-translate-y-0.5 transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-300`. Host bindings: `tabindex="0"` when clickable; handle Enter/Space to emit `clicked`.

### Step 4: AppFormField
**Files:** `client/src/app/shared/components/app-form-field/app-form-field.component.{ts,html}`
**Action:** Create
Inputs: `label`, `helperText`, `error` (signal), `required: boolean`. Renders:
```html
<label class="block">
  <span class="block text-sm font-medium text-slate-700 mb-1">
    {{ label() }} <span *ngIf="required()" class="text-red-500">*</span>
  </span>
  <ng-content />
  <p *ngIf="error()" class="mt-1 text-sm text-red-600" [attr.aria-live]="'polite'">{{ error() }}</p>
  <p *ngIf="!error() && helperText()" class="mt-1 text-xs text-slate-500">{{ helperText() }}</p>
</label>
```
Use `@ContentChild('input')` (or a directive) to set `aria-invalid` on the projected control when `error()` is truthy. Document that consumers must style their projected `<input>` with: `block w-full border border-slate-300 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 rounded-lg px-3 h-10`.

### Step 5: AppErrorBanner
**Files:** `client/src/app/shared/components/app-error-banner/app-error-banner.component.{ts,html}`
**Action:** Create
Input: `error: AppError | null` (type lives at `core/errors/app-error.ts` — added in T-013, but can be a temp `{ title; detail?; correlationId? }` here and rewritten in T-013 without API change). Output: `retry` (when consumer provides a slot). Renders an alert with `role="alert"`, `bg-red-50 border border-red-200 text-red-700 rounded-lg p-4 flex items-start gap-3`. Includes copy-correlationId button when `correlationId` is present.

### Step 6: EmptyState
**Files:** `client/src/app/shared/components/empty-state/empty-state.component.{ts,html}`
**Action:** Create
Inputs: `title`, `description`, `cta?: { label; click: EventEmitter<void> }` (or projected slot). Centered, `py-12 text-center`. Icon slot via `<ng-content select="[empty-icon]">`. Description in `text-slate-500`.

### Step 7: Barrel export
**File:** `client/src/app/shared/index.ts`
**Action:** Create
Re-export all six components. Consumers `import { AppButton, AppCard, ... } from '@app/shared';`. Configure `tsconfig.json` `paths` for `@app/*`.

### Step 8: Specs
**Files:** `*/<component>.component.spec.ts` (×6)
**Action:** Create
For each: render the component, assert default classes per variant, simulate disabled/loading where applicable, simulate keyboard activation for AppCard, verify `aria-invalid` toggling on AppFormField, verify `role="alert"` on AppErrorBanner.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `mockups/foundational-components.html` | Create | Stakeholder-approved mockup |
| `client/src/app/shared/components/app-spinner/*.{ts,html,spec.ts}` | Create | Spinner |
| `client/src/app/shared/components/app-button/*` | Create | Button |
| `client/src/app/shared/components/app-card/*` | Create | Card |
| `client/src/app/shared/components/app-form-field/*` | Create | Form field |
| `client/src/app/shared/components/app-error-banner/*` | Create | RFC 7807 banner |
| `client/src/app/shared/components/empty-state/*` | Create | Empty-state placeholder |
| `client/src/app/shared/index.ts` | Create | Barrel export |
| `client/tsconfig.json` | Modify | `paths: { "@app/*": ["src/app/*"] }` |

## Edge Cases & Risks
- **Tailwind purge missing dynamic classes** — Tailwind 4's content-detection covers `.html` and `.ts`; computed class strings inside `computed()` are picked up as long as the strings appear literally. Avoid building class names by concatenation.
- **Focus ring on dark hover backgrounds** — `focus-visible:ring-sky-300` may be invisible on `bg-sky-500` (primary). Use `focus-visible:ring-offset-2 focus-visible:ring-offset-white` for contrast.
- **AppFormField + reactive forms ControlValueAccessor** — the form-field is a wrapper, not a CVA. Consumers project their own `<input formControlName="...">` inside. Validate this works with template-driven and reactive forms in the spec.

## Acceptance Verification
- [ ] Mockup approved before implementation (per `mockup-first` workflow).
- [ ] All six components exist as standalone with `.ts` + `.html`; no `.css` or `.scss` files anywhere in `shared/components/`.
- [ ] AppButton renders all four variants and the `loading` state with inline spinner.
- [ ] AppCard clickable variant applies hover lift and visible focus ring.
- [ ] AppFormField sets `aria-invalid="true"` on the projected control when `error` is set.
- [ ] AppErrorBanner renders `title`, `detail`, and `correlationId`.
- [ ] `ng test --watch=false` passes; every component has ≥1 spec covering its key states.
