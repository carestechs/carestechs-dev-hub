# Implementation Plan: T-033 — Frontend Executor bindings admin screen

## Task Reference
- **Task ID:** T-033
- **Type:** Frontend
- **Workflow:** mockup-first
- **Complexity:** M
- **Rationale:** FEAT-003 AC-1 — "register a second executor of a known shape with zero code changes" relies on the operator having a UI to bind the project type.

## Overview
Single screen at `/admin/executor-bindings` with the standard list + create-modal + delete-confirm shape. Reuses `executor-registry.service.ts` from T-032.

## Implementation Steps

### Step 0: Mockup
**File:** `mockups/admin-executor-bindings.html` · Create

Sections:
- Page header, "+ New binding" primary button.
- `AppTable` columns: Project type (mono), Executor (display name + key chip), Status pill (from the executor), Created.
- Row actions: Delete only (no edit — delete + recreate per the API contract).
- Create modal: `projectType` (lowercase + hyphen validator), executor dropdown (active executors only).
- Delete confirm with the warning copy from the AC.

Submit to user for approval.

### Step 1: Page component
**Files (Create):**
- `client/src/app/features/admin/executor-bindings/executor-bindings.page.{ts,html,spec.ts}`

Standalone, OnPush. Signals for `bindings`, `executors` (active only), `loading`, `error`. On mount, fire both `executorRegistry.listBindings()` and `executorRegistry.list({ status: 'Active' })` in parallel (Promise.all pattern). Methods: `load()`, `openNew()`, `askDelete(b)`, `onModalSubmit(req)`, `onDeleteConfirm()`.

### Step 2: Binding form modal
**Files (Create):**
- `client/src/app/features/admin/executor-bindings/binding-form.modal.{ts,html,spec.ts}`

Inputs: `open`, `executors`, `working`, `serverError`. Outputs: `submitted({ projectType, executorId })`, `cancelled`.

Form:
- `projectType`: text, required, pattern `^[a-z0-9-]+$` (mirror Project slug rules from FEAT-002).
- `executorId`: select, required. Options show `displayName — key`.

Surface 409 inline via `AppErrorBanner` inside the modal.

### Step 3: Routes
**File:** `client/src/app/app.routes.ts` · Modify
Replace the placeholder `/admin/executor-bindings` route with a real lazy import + `operatorGuard`.

### Step 4: Specs
- `executor-bindings.page.spec.ts`: parallel load (use the FEAT-002 microtask pattern), renders rows, create flow refreshes, delete flow refreshes, 409 inline.
- `binding-form.modal.spec.ts`: projectType validation, executor dropdown population, submit emits.

## Files Affected
| File | Action |
|------|--------|
| `mockups/admin-executor-bindings.html` | Create |
| `features/admin/executor-bindings/executor-bindings.page.{ts,html,spec.ts}` | Create |
| `features/admin/executor-bindings/binding-form.modal.{ts,html,spec.ts}` | Create |
| `app.routes.ts` | Modify |

## Edge Cases & Risks
- **Empty executors list** — if no Active executors exist, the create button should still be enabled but the modal should show a friendly "Register an executor first" empty state inside the dropdown (or disable submit). Decide in the mockup.
- **Delete warning copy** — exact text: "Existing projects of type `<projectType>` can still read state, but you cannot create new projects of this type until a new binding is registered." Render the project type as a mono chip.
- **Edit-by-delete-and-recreate** — there is intentionally no Edit action. If the operator wants to redirect a `projectType` to a different executor, they delete + recreate. Add a small helper text under the table: "To change a binding, delete and re-create it."

## Acceptance Verification
- [ ] Mockup approved before implementation.
- [ ] `ng test` for the new specs is green.
- [ ] `ng build` is clean.
- [ ] Manual smoke: log in as operator, register an executor (T-032), bind `feature-delivery` → that executor, attempt duplicate (expect inline 409), delete, attempt to create a project of that type via `/projects` (expect 409 from the API, surfaced in the project create flow).
