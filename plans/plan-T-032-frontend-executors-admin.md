# Implementation Plan: T-032 — Frontend Executors admin screen

## Task Reference
- **Task ID:** T-032
- **Type:** Frontend
- **Workflow:** mockup-first
- **Complexity:** M
- **Rationale:** FEAT-003 AC-5 — operators must register/inspect executors + contracts via the SPA, not curl.

## Overview
New operator-only screen at `/admin/executors` mirroring the T-028 admin shape (list + create/edit modal + delete confirm + 400/409 inline errors). One extra modal for replace-contracts (the contracts FormArray pattern). New `executor-registry.service.ts` (sibling to `workspace.service.ts`) for the typed HTTP wrappers.

## Implementation Steps

### Step 0: Mockup
**File:** `mockups/admin-executors.html` · Create
Follow `mockups/admin-teams.html` shell. Sections:
- Page header, "+ Register executor" primary button.
- `AppTable` with columns: Key (mono), Display name, Base URL (mono, truncated), Status pill, Contracts count, Created.
- Row actions row below the table (parallel `<ul>` pattern from T-028): Edit · Replace contracts · Delete.
- Register modal: Key, Display name, Base URL, `credentialsRef` (with the "reference, not the secret" info tooltip), dynamic contracts editor.
- Replace-contracts modal: same contracts editor, prefilled.
- Delete confirm dialog with "Cannot delete: 2 active bindings" 409 affordance.

Submit to the user for approval before implementation.

### Step 1: Types + service
**Files (Create):**
- `client/src/app/core/api/executor-registry.types.ts` — `ExecutorDto`, `CheckpointContractDto`, `CreateExecutorRequest`, `UpdateExecutorRequest`, `ReplaceContractsRequest`, `ExecutorStatus = 'Active' | 'Paused' | 'Retired'`.
- `client/src/app/core/api/executor-registry.service.ts` — typed methods for: `list(query)`, `create(req)`, `get(id)`, `patch(id, req)`, `replaceContracts(id, req)`, `delete(id)`, `listBindings(query)`, `createBinding(req)`, `deleteBinding(id)`.

Same `{ data, meta }` unwrap pattern as `workspace.service.ts`. Return signals where convenient; HTTP path returns observables.

### Step 2: Page component
**Files (Create):**
- `client/src/app/features/admin/executors/executors.page.ts`
- `client/src/app/features/admin/executors/executors.page.html`
- `client/src/app/features/admin/executors/executors.page.spec.ts`

Standalone, OnPush. Signals for `executors`, `loading`, `error`. Methods: `load()`, `openRegister()`, `openEdit(e)`, `openReplaceContracts(e)`, `askDelete(e)`, `onModalSubmit(...)`, `onReplaceSubmit(...)`, `onDeleteConfirm()`.

### Step 3: Executor form modal (register + edit)
**Files (Create):**
- `client/src/app/features/admin/executors/executor-form.modal.{ts,html,spec.ts}`

Inputs: `open`, `mode: 'create' | 'edit'`, `executor: ExecutorDto | null`, `availableRoles: RoleDto[]`, `working`, `serverError`.
Outputs: `submitted(request)`, `cancelled`.

Form:
- `key`: text, required on create, read-only on edit.
- `displayName`: text, required.
- `baseUrl`: URL, required.
- `credentialsRef`: text, required.
- On create only: contracts `FormArray` with the T-028 fieldset-guard pattern. Each row: `checkpointKey`, `displayName`, `requiredRoleKey` (select from `availableRoles`), `allowedOutcomes` (comma-separated text, parsed on submit).

`requiredRoleKey` dropdown sourced from `WorkspaceService.listRoles()`.

### Step 4: Replace-contracts modal
**Files (Create):**
- `client/src/app/features/admin/executors/contracts-form.modal.{ts,html,spec.ts}`

Simpler: same contracts editor as above, no other fields. Prefilled from the executor's existing contracts. Warns "Replacing contracts is atomic; in-flight signals for removed contracts will 404." (matches the FEAT-004 reader rule).

### Step 5: Routes + sidebar
**File:** `client/src/app/app.routes.ts` · Modify
Replace the placeholder `/admin/executors` route with a real lazy import + `operatorGuard`.

**File:** `client/src/app/core/layouts/app-shell/sidebar.html` · Modify (only if the placeholder needs swap)
Verify the link points at `/admin/executors`.

### Step 6: Specs
- `executors.page.spec.ts`: load renders rows; non-operator redirect (provide a non-operator `AuthService` mock); register modal open submits; delete 409 surfaces inline.
- `executor-form.modal.spec.ts`: validation (key required, baseUrl url), edit makes key read-only, submit emits the request payload.
- `contracts-form.modal.spec.ts`: prefilled with existing contracts; allowedOutcomes parses on submit; submit emits.

Use the test microtask pattern established in FEAT-002 specs (4–6 `await Promise.resolve()` cycles for POST→refresh chains).

## Files Affected
| File | Action |
|------|--------|
| `mockups/admin-executors.html` | Create |
| `core/api/executor-registry.{types,service}.ts` | Create |
| `features/admin/executors/executors.page.{ts,html,spec.ts}` | Create |
| `features/admin/executors/executor-form.modal.{ts,html,spec.ts}` | Create |
| `features/admin/executors/contracts-form.modal.{ts,html,spec.ts}` | Create |
| `app.routes.ts` | Modify |

## Edge Cases & Risks
- **`credentialsRef` UX** — display as monospace with a small info icon. Copy: "Reference to an env var on the API host. The actual secret value never leaves the server." Do NOT add a "show value" toggle (there's nothing to show — the API doesn't return the resolved value).
- **Contracts FormArray sync** — use the same `@if (form.controls.contracts.length === backing.length)` guard pattern as the memberships modal from T-028.
- **`allowedOutcomes` parsing** — `"approve, reject, revise"` → `["approve","reject","revise"]`. Trim + lowercase + filter empties. Reject if the resulting list is empty.
- **Page contract count column** — render `contracts.length`. Update after a replace.

## Acceptance Verification
- [ ] Mockup approved before implementation.
- [ ] `ng test` for the new specs is green.
- [ ] `ng build` is clean.
- [ ] Manual smoke: log in as operator, register `feature-delivery-v1` with one `approve` contract, edit display name, replace contracts, attempt delete with a binding (expect inline 409), delete the binding then delete the executor.
