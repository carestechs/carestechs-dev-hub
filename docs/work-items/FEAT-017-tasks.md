# FEAT-017 Tasks — Mockup Checkpoint

> Feature Brief: `docs/work-items/FEAT-017-mockup-checkpoint.md`
> Orchestrator spec: `carestechs-agent-orchestrator/docs/lifecycle-v05-manual-devhub-integration.md`

---

## Group 1 — Backend

### T-109: `CheckpointDerivation` — add `confirm_mockup` identity mapping

**Type:** Backend
**Workflow:** standard
**Complexity:** S
**Dependencies:** None

**Description:**
`CheckpointDerivation.SignalForNodeName` applies a generic `confirm_<X>` → `<X>-confirmed` rule. Without a special case, `confirm_mockup` would produce `"mockup-confirmed"`, which does not match the orchestrator's expected signal name or DevHub's intended checkpoint key. Add an explicit `case "confirm_mockup": return "confirm_mockup";` before the generic suffix pattern so the node name is preserved as-is.

**Rationale:**
`confirm_mockup` is the only `confirm_*` node whose DevHub checkpoint key does not follow the `X-confirmed` pattern — it maps to `"confirm_mockup"` so it can be distinguished in the `@switch` and resolved to `"mockup-approved"` in the signal step.

**Acceptance Criteria:**
- [ ] `CheckpointDerivation.SignalForNodeName("confirm_mockup")` returns `"confirm_mockup"`.
- [ ] Existing mappings (`confirm_brief` → `"brief-confirmed"`, `confirm_tasks` → `"tasks-confirmed"`, etc.) are unaffected.

**Files to Modify/Create:**
- `src/DevHub.Modules.WorkItems/Services/Orchestrator/CheckpointDerivation.cs` — add `case "confirm_mockup": return "confirm_mockup";` before the generic suffix block.

**Technical Notes:**
Place the special case at the top of the switch, before the `confirm_` prefix block, so it short-circuits the generic rule.

---

### T-110: `OrchestratorExecutorClient` — signal name resolver for `confirm_mockup`

**Type:** Backend
**Workflow:** standard
**Complexity:** S
**Dependencies:** T-109

**Description:**
`OrchestratorExecutorClient.SignalAsync` currently sends `name = checkpointKey` verbatim to the orchestrator. For `confirm_mockup` the orchestrator expects signal name `"mockup-approved"`, not `"confirm_mockup"`. Add a private `ResolveSignalName(string checkpointKey)` helper with a switch expression that maps `"confirm_mockup"` → `"mockup-approved"` and falls through to `checkpointKey` for all other values. Use it in the body construction at line 140/141.

**Rationale:**
Decouples DevHub's internal checkpoint key (used in the UI switch and auditing) from the orchestrator's signal name vocabulary. All existing signals are identity mappings so existing behaviour is unchanged.

**Acceptance Criteria:**
- [ ] Signal sent to the orchestrator for `confirm_mockup` carries `name = "mockup-approved"`.
- [ ] All other checkpoint keys continue to send `name = checkpointKey` (no regression).

**Files to Modify/Create:**
- `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs` — add `ResolveSignalName` helper; replace `name = checkpointKey` with `name = ResolveSignalName(checkpointKey)` in the body builder.

**Technical Notes:**
```csharp
private static string ResolveSignalName(string checkpointKey) => checkpointKey switch
{
    "confirm_mockup" => "mockup-approved",
    _ => checkpointKey,
};
```

---

### T-111: Register `confirm_mockup` checkpoint contract

**Type:** Backend
**Workflow:** standard
**Complexity:** S
**Dependencies:** None

**Description:**
Add `confirm_mockup` to the set of checkpoint contracts registered for the `lifecycle-agent` executor. Contract: `checkpointKey = "confirm_mockup"`, `displayName = "Mockup Review"`, `requiredRoleKey = "operator"`, `allowedOutcomes = ["approve", "reject"]`. This follows the same registration path as the six existing contracts (`brief-confirmed`, `tasks-confirmed`, `assignment-confirmed`, `plan-confirmed`, `implementation-complete`, `review-completed`).

**Rationale:**
The generic signal endpoint validates that `outcome` is in `allowedOutcomes` for the contract. Without registration, signalling `confirm_mockup` returns 404.

**Acceptance Criteria:**
- [ ] `GET /api/admin/executors/{id}` lists `confirm_mockup` in `checkpointContracts`.
- [ ] `POST .../checkpoints/confirm_mockup/signal` with `outcome = "approve"` returns 200.
- [ ] `POST .../checkpoints/confirm_mockup/signal` with `outcome = "reject"` returns 200.
- [ ] `POST .../checkpoints/confirm_mockup/signal` with `outcome = "revise"` returns 400.

**Files to Modify/Create:**
- Find where the six existing contracts are seeded for the `lifecycle-agent` executor (executor registration fixture or seed script) and add the `confirm_mockup` row alongside them.

**Technical Notes:**
Check `DevHubApiFactory` in the test harness and any seed/migration files that register the existing 6 contracts — add `confirm_mockup` in the same place so all test suites pick it up automatically.

---

## Group 2 — Frontend

### T-112: `TaskListReviewPanel` — `kind` badge for mockup tasks

**Type:** Frontend
**Workflow:** standard
**Complexity:** S
**Dependencies:** None

**Description:**
The task list at `confirm_tasks` surfaces `nodeInputs.tasks[]`, each of which now carries a `kind` field (`"feature"` | `"mockup"` | `"bug"` | `"chore"`). Add a small inline badge next to the task title for tasks with `kind="mockup"`. All other kinds render no badge (kind is implicit / default).

**Rationale:**
Operators need to know before approving the task list that a mockup review step is coming for specific tasks — so the checkpoint is not a surprise mid-flow (AC-5, AC-6).

**Acceptance Criteria:**
- [ ] Tasks with `kind="mockup"` show a `Mockup` badge (sky-100 bg / sky-700 text, `text-xs font-medium px-1.5 py-0.5 rounded`).
- [ ] Tasks with `kind="feature"`, `"bug"`, `"chore"`, or absent `kind` show no badge.
- [ ] Badge is purely decorative — it does not affect the approve/reject flow.

**Files to Modify/Create:**
- `client/src/app/features/projects/work-items/components/task-list-review-panel.ts` — update `Task` type to include optional `kind?: string`.
- `client/src/app/features/projects/work-items/components/task-list-review-panel.html` — add `@if (task.kind === 'mockup')` badge alongside task title.

**Technical Notes:**
`kind` is backward-compatible — old runs omit it. Treat absent/unknown `kind` the same as `"feature"` (no badge). Do not add badges for `"bug"` or `"chore"` in this FEAT — only `"mockup"` is visually significant at this checkpoint.

---

### T-113: `MockupReviewPanel` — new component

**Type:** Frontend
**Workflow:** standard
**Complexity:** M
**Dependencies:** None

**Description:**
Create `MockupReviewPanel`, the purpose-built panel for the `confirm_mockup` checkpoint. Reads `nodeInputs.mockupHtml`, `nodeInputs.mockupDescription`, and `nodeInputs.currentTask` from the `artefact` input. Renders the HTML in a sandboxed `<iframe srcdoc>`. Provides an "Approve" button (emits `{ outcome: "approve" }`) and a "Request changes" toggle that reveals a feedback textarea (emits `{ outcome: "reject", payload: { feedback } }` when submitted; submit disabled while textarea is empty).

**Rationale:**
Purpose-built panel replaces the generic `CheckpointActionBar` for this checkpoint, matching the pattern established by `BriefReviewPanel`, `PlanReviewPanel`, and `ImplementationReviewPanel` (AC-1, AC-2, AC-3, AC-4, AC-7).

**Acceptance Criteria:**
- [ ] `mockupHtml` rendered in `<iframe srcdoc="..." sandbox="allow-same-origin allow-scripts" class="w-full ...">`. Never injected into the DOM directly.
- [ ] `mockupDescription` shown as a caption below the iframe.
- [ ] `currentTask.title` and `currentTask.description` shown as context above the iframe.
- [ ] "Approve" button emits `SubmittedOutcome` with `{ outcome: "approve" }`.
- [ ] "Request changes" button reveals a textarea; submit emits `{ outcome: "reject", payload: { feedback: <text> } }`.
- [ ] Reject submit button is disabled when the textarea is empty (AC-7).
- [ ] Component follows the `artefact = input<unknown>(null)` pattern used by other panels.

**Files to Modify/Create:**
- `client/src/app/features/projects/work-items/components/mockup-review-panel.ts` — new standalone component.
- `client/src/app/features/projects/work-items/components/mockup-review-panel.html` — template.

**Technical Notes:**
Follow the `ImplementationReviewPanel` pattern for the approve/reject toggle (it already has a reject-with-feedback path). The `sandbox` attribute on the iframe should include `allow-same-origin allow-scripts` so the self-contained HTML with inline CSS/JS renders correctly. Iframe height: `min-h-[600px]` with `w-full`. Use `[attr.srcdoc]` binding, not `innerHTML`.

---

### T-114: `review.page` — wire `MockupReviewPanel` into the checkpoint switch

**Type:** Frontend
**Workflow:** standard
**Complexity:** S
**Dependencies:** T-113

**Description:**
Add `confirm_mockup` as a case in the `@switch (wi.currentCheckpointKey)` block in `review.page.html`, rendering `<mockup-review-panel>`. Import `MockupReviewPanel` in `review.page.ts` and add it to the component's `imports` array. Wire the panel's submit output to the existing `onSignal` handler.

**Rationale:**
All other purpose-built panels are wired here — `confirm_mockup` must follow the same pattern so the review page routes to the correct panel when the work item is at that checkpoint (AC-1).

**Acceptance Criteria:**
- [ ] When `wi.currentCheckpointKey === "confirm_mockup"`, the review page renders `MockupReviewPanel`.
- [ ] Approving from the panel triggers `onSignal({ outcome: "approve" })`.
- [ ] Rejecting with feedback triggers `onSignal({ outcome: "reject", payload: { feedback } })`.
- [ ] No other checkpoint case is changed.

**Files to Modify/Create:**
- `client/src/app/features/projects/work-items/review.page.html` — add `@case ("confirm_mockup")` block.
- `client/src/app/features/projects/work-items/review.page.ts` — import and register `MockupReviewPanel`.

---

## Group 3 — Testing

### T-115: Backend integration tests — `confirm_mockup` signal

**Type:** Testing
**Workflow:** standard
**Complexity:** M
**Dependencies:** T-109, T-110, T-111

**Description:**
Add integration tests covering the `confirm_mockup` checkpoint signal path. Use the existing `[Collection("postgres")]` pattern with `DevHubApiFactory` and the fake executor scripted to pause at `confirm_mockup`. Test: approve advances the work item; reject with feedback returns 200 and the feedback reaches the executor; unknown outcome returns 400; signal without auth returns 401.

**Rationale:**
Authorization is a tested concern. Every new checkpoint that wraps an executor call must ship with at least one deny-path test (per CLAUDE.md PR requirements).

**Acceptance Criteria:**
- [ ] `approve` → 200, work item advances past `confirm_mockup`.
- [ ] `reject` with `payload.feedback` → 200.
- [ ] `revise` (not in `allowedOutcomes`) → 400.
- [ ] Unauthenticated request → 401.
- [ ] Non-operator role → 403.

**Files to Modify/Create:**
- `tests/DevHub.Modules.WorkItems.Tests/MockupCheckpointTests.cs` — new test class.

**Technical Notes:**
Script the fake executor to return `currentCheckpointKey = "confirm_mockup"` and `currentStatus = "WaitingOnCheckpoint"` on fetch, then advance on signal. Reuse the `SeedContractAsync` helper pattern from existing test classes.

---

### T-116: Angular unit tests — `MockupReviewPanel` and task kind badge

**Type:** Testing
**Workflow:** standard
**Complexity:** S
**Dependencies:** T-112, T-113

**Description:**
Unit tests for `MockupReviewPanel` (iframe srcdoc binding, approve emit, reject emit, disabled state) and for the `kind` badge in `TaskListReviewPanel` (badge present for `"mockup"`, absent for `"feature"` and absent when `kind` is undefined).

**Rationale:**
Covers AC-2, AC-3, AC-4, AC-7 from the test acceptance criteria in the feature brief.

**Acceptance Criteria:**
- [ ] `MockupReviewPanel`: iframe `srcdoc` equals the provided `mockupHtml`.
- [ ] `MockupReviewPanel`: "Approve" click emits `{ outcome: "approve" }`.
- [ ] `MockupReviewPanel`: reject submit emits `{ outcome: "reject", payload: { feedback: "..." } }`.
- [ ] `MockupReviewPanel`: reject submit button disabled when feedback textarea is empty.
- [ ] `TaskListReviewPanel`: `kind="mockup"` task shows badge with text "Mockup".
- [ ] `TaskListReviewPanel`: `kind="feature"` task shows no badge.
- [ ] `TaskListReviewPanel`: task without `kind` field shows no badge.

**Files to Modify/Create:**
- `client/src/app/features/projects/work-items/components/mockup-review-panel.spec.ts` — new spec.
- `client/src/app/features/projects/work-items/components/task-list-review-panel.spec.ts` — update existing spec (or create if absent).

---

## Group 4 — Documentation

### T-117: Documentation updates — ui-specification, api-spec, brief status

**Type:** Documentation
**Workflow:** standard
**Complexity:** S
**Dependencies:** T-109, T-110, T-111, T-112, T-113, T-114

**Description:**
Update `docs/ui-specification.md` to document `MockupReviewPanel` and the `kind` badge in `TaskListReviewPanel`. Add a changelog entry to `docs/api-spec.md` noting the `confirm_mockup` contract and the `ResolveSignalName` mapping. Update `docs/work-items/FEAT-017-mockup-checkpoint.md` status to Completed.

**Rationale:**
Per the CLAUDE.md maintenance table: new/changed components update `ui-specification.md`; new/changed endpoint contracts update `api-spec.md`; feature completion updates the brief status.

**Acceptance Criteria:**
- [ ] `ui-specification.md` changelog entry for FEAT-017 lists `MockupReviewPanel` and `kind` badge.
- [ ] `api-spec.md` changelog entry notes `confirm_mockup` contract and `mockup-approved` signal name.
- [ ] `FEAT-017-mockup-checkpoint.md` Status → Completed, all acceptance criteria checked.

**Files to Modify/Create:**
- `docs/ui-specification.md` — add changelog entry.
- `docs/api-spec.md` — add changelog entry.
- `docs/work-items/FEAT-017-mockup-checkpoint.md` — Status + AC checkboxes.

---

## Summary

| Group | Tasks | Types |
|-------|-------|-------|
| Backend | T-109, T-110, T-111 | Backend ×3 |
| Frontend | T-112, T-113, T-114 | Frontend ×3 |
| Testing | T-115, T-116 | Testing ×2 |
| Documentation | T-117 | Documentation ×1 |
| **Total** | **9** | |

**Complexity distribution:** S ×7, M ×2

**Critical path:** T-109 → T-110 → T-115 (backend chain, 3 hops). Frontend tasks T-112 and T-113 are independent and can run in parallel with the backend chain. T-114 depends on T-113.

**Risks / open questions:**
- The `confirm_mockup` contract registration location (T-111) needs verification — find where the existing 6 contracts are seeded in the executor registration setup and add there, not in a separate place.
- Iframe sandbox flags (`allow-same-origin allow-scripts`) — verify the generated mockup HTML does not require `allow-forms` or other flags. If the mockup includes a `<form>`, add `allow-forms`.
- `mockupHtml` size — LLM-generated HTML can be large. No truncation needed; the iframe handles it. Confirm the `nodeInputs` payload passes through DevHub's signal response without size limits.
