# Implementation Plan: T-061 — Project create modal + detail edit affordance

## Task Reference
- **Task ID:** T-061 · **Type:** Frontend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-8, plus the soft-warning banner. Workflow is `standard` per the prompt's CRUD-screen exception — the form mockup was approved in PR #45.

## Overview
Two UI surfaces: the create modal (PR #45's form) gains two optional fields; the project detail page gains a read-only display + operator-gated inline edit + soft amber warning banner when no repo is set.

## Implementation Steps

### Step 1: Extend the create-modal form
**File:** `client/src/app/features/projects/project-form.modal.ts` · Modify

Add two new form controls in the existing `FormGroup`:

```ts
repo: new FormControl<string>('', {
  nonNullable: true,
  validators: [Validators.maxLength(140), Validators.pattern(/^[A-Za-z0-9._-]+\/[A-Za-z0-9._-]+$/)],
}),
defaultBranch: new FormControl<string>('', {
  nonNullable: true,
  validators: [Validators.maxLength(200), branchValidator()],
}),
```

Add a small `branchValidator` factory near the top of the file: rejects leading `/`, `..`, whitespace, and control chars — mirror the C# rules from T-056.

Update `onSubmit` to include both fields in the emitted `CreateProjectRequest`, sending `undefined` (not `""`) when empty:

```ts
this.submitted.emit({
  name, slug, projectType, owningTeamId,
  description: description || undefined,
  repo: repo || undefined,
  defaultBranch: defaultBranch || undefined,
});
```

### Step 2: Render the new fields in the modal template
**File:** `client/src/app/features/projects/project-form.modal.html` · Modify

Add two `<app-form-field>` blocks after "Project type":

```html
<app-form-field label="Repo (owner/name)" [error]="repoError()">
  <input formControlName="repo" placeholder="acme/widgets" autocomplete="off"
         class="block w-full border border-slate-300 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 rounded-lg px-3 h-10 outline-none font-mono text-sm" />
</app-form-field>
<app-form-field label="Default branch" [error]="defaultBranchError()">
  <input formControlName="defaultBranch" placeholder="main" autocomplete="off"
         class="block w-full border border-slate-300 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 rounded-lg px-3 h-10 outline-none font-mono text-sm" />
</app-form-field>
```

Add the two corresponding error-getter methods alongside `nameError()` / `slugError()`.

### Step 3: Render code-source on the project detail page
**File:** `client/src/app/features/projects/project-home.page.html` · Modify

In the project metadata strip (next to Project type / Owning team), add:

```html
<div>
  <p class="text-xs text-slate-500">Repo</p>
  @if (project()?.repo) {
    <a [href]="'https://github.com/' + project()!.repo" target="_blank" rel="noopener"
       class="text-sm text-sky-600 hover:underline font-mono">{{ project()!.repo }}</a>
  } @else {
    <p class="text-sm text-slate-400 italic">(not set)</p>
  }
</div>
<div>
  <p class="text-xs text-slate-500">Default branch</p>
  <p class="text-sm font-mono">{{ project()?.defaultBranch ?? '(not set)' }}</p>
</div>
```

Add an operator-only "Edit code source" button next to the strip → opens an inline form (re-use `project-form.modal` in "edit code source only" mode, or a new lightweight `code-source-edit.modal` — pick the simpler path).

### Step 4: Render the amber warning banner
**File:** `client/src/app/features/projects/project-home.page.html` · Modify

Above the metadata strip, when `isOperator() && !project()?.repo`:

```html
@if (isOperator() && !project()?.repo) {
  <div class="rounded-lg bg-amber-50 border border-amber-200 text-amber-800 px-4 py-3 mb-6 text-sm">
    <strong>No repo set on this project.</strong> Once the orchestrator flips the strict flag,
    starting work items will fail. Click <em>Edit code source</em> to set the repo and default branch.
  </div>
}
```

### Step 5: Wire the inline edit
**File:** `client/src/app/features/projects/project-home.page.ts` · Modify

Add `codeSourceOpen`, `codeSourceSaving`, `codeSourceError` signals. `openCodeSourceEdit()` toggles the modal; `submitCodeSource(req: UpdateProjectRequest)` calls `WorkspaceService.updateProject` and refreshes the project signal from the response. Non-operators do not see the button.

### Step 6: Update existing specs
**Files:**
- `client/src/app/features/projects/project-form.modal.spec.ts` · Modify
- `client/src/app/features/projects/project-home.page.spec.ts` · Modify

Modal spec: add cases for "repo invalid → submit blocked" and "repo + defaultBranch round-trip into the emitted request". Detail-page spec: add cases for "banner renders when repo null and viewer is operator" and "banner hidden for non-operators".

### Step 7: Build + smoke
**Bash:**

```bash
cd client && npx ng build --configuration development
npx ng test --watch=false --browsers=ChromeHeadless
```

Manual smoke: log in as operator → Projects → + New project → fill repo + defaultBranch → submit → land on the new project detail page → banner is absent → click Edit code source → change → save → banner stays absent.

## Files Affected
| File | Action |
|------|--------|
| `client/src/app/features/projects/project-form.modal.ts` | Modify |
| `client/src/app/features/projects/project-form.modal.html` | Modify |
| `client/src/app/features/projects/project-home.page.ts` | Modify |
| `client/src/app/features/projects/project-home.page.html` | Modify |
| `client/src/app/features/projects/project-form.modal.spec.ts` | Modify |
| `client/src/app/features/projects/project-home.page.spec.ts` | Modify |

## Edge Cases & Risks
- **Half-set fields on submit.** If the user fills `repo` but not `defaultBranch` (or vice versa), the form still submits — both are independently optional. The backend T-059 omits the codeSource block entirely if either is missing; the banner stays visible until both are set. Consider tightening to "if one is set, both must be set" in a follow-up if the half-set pattern proves confusing.
- **Modal reuse vs. dedicated edit modal.** If the create-modal's logic ends up entangled with "all fields required for create vs. only code-source fields for edit", split it into a small dedicated `code-source-edit.modal` rather than passing a mode flag. Cleaner.
- **GitHub link is unsafe if `repo` contains characters not allowed by URL syntax.** The backend regex restricts repo to `[A-Za-z0-9._-]+/[A-Za-z0-9._-]+` — all URL-safe. Defense in depth: the template doesn't need any escaping.

## Acceptance Verification
- [ ] Create modal shows the two new fields; client-side validation blocks invalid values.
- [ ] Detail page shows the two new metadata rows.
- [ ] Edit affordance only appears for operators.
- [ ] Soft amber banner appears when repo is null AND viewer is operator; clears after a valid save.
- [ ] All component specs green; build clean.
