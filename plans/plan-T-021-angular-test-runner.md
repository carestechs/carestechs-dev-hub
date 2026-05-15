# Implementation Plan: T-021 — Angular spec scaffolding + AuthService and shared-component specs

## Task Reference
- **Task ID:** T-021
- **Type:** Testing
- **Workflow:** standard
- **Complexity:** M
- **Rationale:** FEAT-001 AC-5 for the frontend side. Establishes the test pattern for later screens.

## Overview
Configure Karma to run headless Chrome non-interactively, ensure the AuthService specs (and the per-component specs authored in T-011/T-012/T-015/T-016) all pass, and verify code coverage emits a report.

## Implementation Steps

### Step 1: Karma headless config
**File:** `client/karma.conf.js`
**Action:** Modify (or Create if `ng new` omitted it under the application builder)
Add a `ChromeHeadlessCI` launcher:
```js
customLaunchers: {
  ChromeHeadlessCI: {
    base: 'ChromeHeadless',
    flags: ['--no-sandbox', '--disable-gpu', '--headless=new']
  }
}
```
Configure `singleRun: true` when launched non-interactively (Angular CLI sets this via `--watch=false`).

### Step 2: angular.json test target
**File:** `client/angular.json`
**Action:** Modify
Under `projects.dev-hub.architect.test.options`:
- `"karmaConfig": "karma.conf.js"`
- `"watch": false` (CI default; dev can still pass `--watch`)
- `"codeCoverage": true`
Add a CI-friendly script entry in `client/package.json`:
```json
"scripts": {
  "test:ci": "ng test --watch=false --browsers=ChromeHeadlessCI --code-coverage"
}
```

### Step 3: Verify pre-existing specs all pass
**Action:** Verify
Run `npm run test:ci`. Confirm the suite includes (from previous tasks):
- T-011 shared component specs (6)
- T-012 layout specs (4: app-shell, public-layout, header, sidebar)
- T-013 auth specs (3: AuthService, authInterceptor, problemDetailsInterceptor)
- T-014 guard specs (1: auth.guard)
- T-015 login.page.spec
- T-016 home.page.spec

### Step 4: Fill any missing specs
**Action:** Conditional
If any of the above is missing from the prior tasks (each task lists its own spec deliverables, but a fast pass might skip one), add it here. Tests must run in headless Chrome with no flakes.

### Step 5: Coverage threshold (advisory, not enforcing in v1)
**File:** `client/karma.conf.js`
**Action:** Modify
Configure `coverageReporter` to emit `lcov` and `text-summary`. Do **not** set a failure threshold in v1 — visibility first, enforcement later (track as an IMP).

### Step 6: Document the test command in README
**File:** `README.md`
**Action:** Modify
Under "Local development", add:
```
# Run all frontend tests (CI mode, headless Chrome)
cd client && npm run test:ci
```

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `client/karma.conf.js` | Modify | ChromeHeadlessCI launcher + coverage reporter |
| `client/angular.json` | Modify | Test target defaults |
| `client/package.json` | Modify | `test:ci` script |
| `README.md` | Modify | Document frontend test command |
| (Conditional) `*.spec.ts` | Create | Backfill missing specs from prior tasks |

## Edge Cases & Risks
- **Headless Chrome in containers** — the `--no-sandbox` flag is required when Karma runs inside Docker as root. CI provider images sometimes mismatch; document.
- **Karma vs the new web-test-runner** — Angular 20 still defaults to Karma for `ng test`. If the team adopts the experimental web-test-runner builder later, the spec files port over unchanged.
- **Flaky time-based specs** — `AuthService.restore()` and `authInterceptor` refresh-and-replay rely on timing in production; specs use `fakeAsync`/`tick` and `HttpTestingController` to make them deterministic. Confirm none of those tests use real `setTimeout`.

## Acceptance Verification
- [ ] `cd client && npm run test:ci` exits 0 with all specs passing.
- [ ] `client/coverage/` exists after the run with `lcov.info` and HTML summary.
- [ ] `AuthService` specs cover login, refresh-on-401, double-401 logout (verified via test names).
- [ ] All shared-component specs from T-011 and layout specs from T-012 are part of the suite.
- [ ] No flakes across 3 consecutive local runs.
