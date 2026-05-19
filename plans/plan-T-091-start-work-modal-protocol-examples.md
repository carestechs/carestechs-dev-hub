# Implementation Plan: T-091 — Per-protocol example payload in `StartWorkModal` (with parent wiring)

## Task Reference
- **Task ID:** T-091
- **Type:** Frontend
- **Workflow:** standard
- **Complexity:** S
- **Dependencies:** T-090 (backend ships `boundExecutorProtocol` on `ProjectDto`).
- **Rationale:** Delivers IMP-001's operator UX win. The modal selects an example payload from a per-protocol constants table; the page forwards `project.boundExecutorProtocol` so the choice is deterministic per project. Frontend-only.

## Overview
Extend the frontend `ProjectDto` type with `boundExecutorProtocol`, add a `protocol` input to `StartWorkModal`, ship per-protocol example constants, bind the textarea's initial value and `placeholder` from those constants, and wire `ProjectHomePage` to forward `project()?.boundExecutorProtocol` to the modal. When the project has no binding (field is `null`), the modal falls back to the orchestrator example.

## Implementation Steps

### Step 1: Extend the frontend `ProjectDto` type
**File:** `client/src/app/core/api/workspace.types.ts`
**Action:** Modify

- Import `ExecutorProtocol` from `./executor-registry.types`.
- Add a new field on `ProjectDto`:
  ```ts
  /** IMP-001. Resolved server-side from the active ExecutorBinding for this
   *  project's projectType. Always `null` on list responses by design. */
  boundExecutorProtocol: ExecutorProtocol | null;
  ```
- Place it at the end of the interface to minimize churn in test fixtures.
- Do not change `CreateProjectRequest` or `UpdateProjectRequest` — those are unchanged on the wire.

### Step 2: Add the per-protocol example constants and `protocol` input on the modal
**File:** `client/src/app/features/projects/work-items/start-work.modal.ts`
**Action:** Modify

- Import `ExecutorProtocol` from `../../../core/api/executor-registry.types`.
- Add `computed` to the existing `@angular/core` import.
- Above the `@Component` decorator, add:
  ```ts
  // Static intake examples. Source of truth for each protocol's shape:
  // - 'orchestrator' → carestechs-agent-orchestrator/src/app/modules/ai/schemas.py § RunCreateRequest
  // - 'devhub'       → IExecutorHttpClient (FakeExecutor) — payload is opaque pass-through
  const EXAMPLE_PAYLOADS: Record<ExecutorProtocol, string> = {
    devhub: '{}',
    orchestrator: `{\n  "task": "Describe the task to run"\n}`,
  };
  const DEFAULT_PROTOCOL: ExecutorProtocol = 'orchestrator';
  ```
- On the class, add the new input alongside the existing ones:
  ```ts
  readonly protocol = input<ExecutorProtocol | null>(null);
  ```
- Add the computed:
  ```ts
  protected readonly exampleJson = computed(() =>
    EXAMPLE_PAYLOADS[this.protocol() ?? DEFAULT_PROTOCOL]
  );
  ```
- Update the reset-on-open `effect()` (currently `start-work.modal.ts:44-51`) so the textarea is reset to the example:
  ```ts
  effect(() => {
    if (!this.open()) return;
    this.form.reset({ title: '', inputJson: this.exampleJson() });
    this.submittedFlag.set(false);
    this.jsonError.set(null);
  });
  ```
- Leave the `FormControl` initial value for `inputJson` as `'{}'` (the effect overwrites it on every open). Leave `Validators.required` semantics unchanged. Leave the JSON-parse path in `onSubmit` unchanged.

### Step 3: Bind the placeholder in the template
**File:** `client/src/app/features/projects/work-items/start-work.modal.html`
**Action:** Modify

- Update the textarea (currently at `start-work.modal.html:22-23`) to bind `[placeholder]="exampleJson()"`:
  ```html
  <textarea formControlName="inputJson" rows="6"
            [placeholder]="exampleJson()"
            class="block w-full border border-slate-300 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 rounded-lg px-3 py-2 outline-none font-mono text-sm"></textarea>
  ```
- No other markup changes. Tailwind classes only.

### Step 4: Forward the protocol from `ProjectHomePage`
**File:** `client/src/app/features/projects/project-home.page.html`
**Action:** Modify

- Update the `<start-work-modal>` element (currently `project-home.page.html:181-187`) to bind `[protocol]`:
  ```html
  <start-work-modal
    [open]="modalOpen()"
    [working]="modalWorking()"
    [serverError]="modalError()"
    [protocol]="project()?.boundExecutorProtocol ?? null"
    (submitted)="onStartSubmitted($event)"
    (cancelled)="onStartCancelled()"
  />
  ```
- No TypeScript changes are required in `project-home.page.ts` — `project()` is already a signal returning `ProjectDto | null`, and TypeScript infers the new field automatically from the updated type.

### Step 5: Extend the modal spec
**File:** `client/src/app/features/projects/work-items/start-work.modal.spec.ts`
**Action:** Modify

- Keep the existing two `it` blocks unchanged.
- Generalize `createOpen()` to accept an optional protocol:
  ```ts
  function createOpen(protocol?: 'devhub' | 'orchestrator') {
    TestBed.configureTestingModule({ imports: [StartWorkModal] });
    const fixture = TestBed.createComponent(StartWorkModal);
    if (protocol) fixture.componentRef.setInput('protocol', protocol);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    return fixture;
  }
  ```
- Add four new specs:
  1. *"defaults to the orchestrator example when no protocol is provided"* — assert `JSON.parse(cmp.form.controls.inputJson.value)` deep-equals `{ task: 'Describe the task to run' }`.
  2. *"uses the devhub example when protocol='devhub'"* — `createOpen('devhub')`; assert `cmp.form.controls.inputJson.value === '{}'` and the textarea's `placeholder` attribute equals `'{}'`.
  3. *"placeholder mirrors the initial value for the orchestrator protocol"* — read `fixture.nativeElement.querySelector('textarea')?.getAttribute('placeholder')` and assert it contains `'"task"'`.
  4. *"submitting the unedited orchestrator example emits a valid parsed payload"* — set `title='Demo'`, call `onSubmit()` without touching `inputJson`, assert one emit with `emitted[0].input` deep-equal to `{ task: 'Describe the task to run' }`.

### Step 6: Add a parent-wiring assertion to `project-home.page.spec.ts`
**File:** `client/src/app/features/projects/project-home.page.spec.ts`
**Action:** Modify

- In the existing setup that loads a project, ensure the fake `ProjectDto` builder sets `boundExecutorProtocol: 'orchestrator'` (or extend the existing fixture).
- Add one assertion: after the page renders, query the `<start-work-modal>` debug element (`fixture.debugElement.query(By.directive(StartWorkModal))`) and assert `componentInstance.protocol() === 'orchestrator'`.
- Extend any other existing `ProjectDto` test fixtures across the spec set if they construct DTOs directly — they need the new `boundExecutorProtocol` field. Default to `null` to preserve prior behavior.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `client/src/app/core/api/workspace.types.ts` | Modify | Add `boundExecutorProtocol` to `ProjectDto`. |
| `client/src/app/features/projects/work-items/start-work.modal.ts` | Modify | Add `protocol` input, `EXAMPLE_PAYLOADS` constants, `exampleJson()` computed; reset-on-open uses the example. |
| `client/src/app/features/projects/work-items/start-work.modal.html` | Modify | Bind `[placeholder]="exampleJson()"` on the JSON textarea. |
| `client/src/app/features/projects/work-items/start-work.modal.spec.ts` | Modify | Four new specs covering protocol-driven initial value, placeholder mirroring, unedited-example submit. |
| `client/src/app/features/projects/project-home.page.html` | Modify | Bind `[protocol]="project()?.boundExecutorProtocol ?? null"` on `<start-work-modal>`. |
| `client/src/app/features/projects/project-home.page.spec.ts` | Modify | Test fixture defaults + one new assertion that protocol is forwarded. |

## Edge Cases & Risks

- **`protocol` set after construction.** Angular `input()` signals can change after the component is created. The `effect()` on `open()` depends on `exampleJson()` (which depends on `protocol()`); the effect will re-fire when `protocol` changes *while the modal is open*. Acceptable — operators rarely flip projects mid-modal, and the worst case is the textarea resets, which is the same behavior as today's open-reset.
- **`project()` is `null` when the page is still loading.** `project()?.boundExecutorProtocol ?? null` already guards this — the modal gets `null` until the project loads, then re-renders with the resolved protocol on the next change-detection cycle.
- **Operator submits the placeholder unchanged.** Captured in IMP-001 §7 risk 2. The orchestrator returns a 502/validation problem detail; the existing `serverError` banner surfaces it. No code path change.
- **Placeholder attribute read in specs.** Use `Element.getAttribute('placeholder')` (string), not `HTMLTextAreaElement.placeholder` (property). Attribute reads have been more consistent across Karma versions for property bindings.
- **Test-fixture sprawl.** Other spec files may build `ProjectDto` directly (e.g. workspace pages, admin views). Run `grep -rn "ProjectDto\b" client/src --include="*.spec.ts"` and add `boundExecutorProtocol: null` wherever a literal `ProjectDto` is constructed, so the type check is satisfied and prior assertions don't change.

## Acceptance Verification

- [ ] `ProjectDto` in `workspace.types.ts` has `boundExecutorProtocol: ExecutorProtocol | null` → grep file.
- [ ] `StartWorkModal` has a `protocol` input + `EXAMPLE_PAYLOADS` constant + `exampleJson()` computed → grep file.
- [ ] Reset-on-open uses `exampleJson()` → read the diff.
- [ ] Textarea binds `[placeholder]="exampleJson()"` → read template diff.
- [ ] `<start-work-modal>` in `project-home.page.html` binds `[protocol]="project()?.boundExecutorProtocol ?? null"` → read template diff.
- [ ] Four new specs in `start-work.modal.spec.ts` → run `cd client && ng test`.
- [ ] Parent wiring spec in `project-home.page.spec.ts` → same `ng test` run.
- [ ] All pre-existing frontend tests pass (other spec files updated for the new `ProjectDto` field) → `ng test` count delta is "+new specs, 0 deletions."
