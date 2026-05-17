# FEAT-009 Task Breakdown — Per-Task Assignment Pause (`assignment-confirmed`)

> Generated from `docs/work-items/FEAT-009-per-task-assignment-pause.md` using `.ai-framework/prompts/feature-tasks.md`. 10 tasks across Database, Backend, Frontend, Testing.

## Scope choices locked in before generation

- **`per_task` is a flag on `CheckpointContract`, not a new entity.** The generalization is the value — `assignment-confirmed` is its first user; future per-task checkpoints on any executor get the same plumbing for free.
- **`WorkItem.CurrentTaskId` is a cache, not a source of truth.** The executor's memory is authoritative (orchestrator IMP-005 `RunMemory.data.current_task_id` / `assignments`). DevHub caches it on every transition so the reconciler can key per-task pending rows without an extra fetch.
- **The `assignment-confirmed` payload contract** is forwarded verbatim to the executor. DevHub's only assignee-level rule is "non-empty string at the boundary" — the executor's idempotency hash is `(run_id, "assignment-confirmed", task_id)` and treats `assignee` as opaque.
- **No DevHub-side persistence of assignments.** The `Assignments` sidebar on the WorkItem detail reads through to the executor's `RunMemory.data.assignments` via the existing fetch-state path. No new table.
- **The lifecycle review page already exists.** T-070 adds a new `AssignmentConfirmPanel` component that the page swaps in when the active checkpoint contract has `perTask=true` AND `checkpointKey="assignment-confirmed"`. Existing `CheckpointActionBar` (used for `approve` etc.) stays untouched. Workflow stays `standard` because the surrounding page layout doesn't change.
- **Sidebar badge auto-counts.** `PendingActionsStore.count` returns `_list().length`. Per-task pending rows are distinct rows, so the badge counts them correctly with no logic change once the reconciler keys per-task.
- **Backwards compatibility:** every new field is nullable / defaults to `false`. Existing fixtures, existing contracts, existing executor responses continue to work — pre-FEAT contracts default to `perTask=false` and pre-FEAT executor responses default to `currentTaskId=null`.

---

## Foundation

### T-064: EF migrations — CheckpointContract.PerTask + PendingActionSignal.TaskId + WorkItem.CurrentTaskId

**Type:** Database · **Workflow:** standard · **Complexity:** S · **Dependencies:** None

**Description:**
Add three nullable / defaulted columns across three modules, plus a uniqueness rewrite on `PendingActionSignal` to use `COALESCE(task_id, '<root>')` as the per-task discriminator. EF migrations in `DevHub.Modules.ExecutorRegistry`, `DevHub.Modules.Notifications`, `DevHub.Modules.WorkItems`.

**Rationale:**
AC-1, AC-3. Foundation — every other task in this FEAT reads from these columns.

**Acceptance Criteria:**
- [ ] `CheckpointContract.PerTask` (`bool`, default `false`) added; existing rows backfill `false`.
- [ ] `WorkItem.CurrentTaskId` (`string?`, max 60) added; nullable.
- [ ] `PendingActionSignal.TaskId` (`string?`, max 60) added; nullable.
- [ ] Notifications DbContext: drop the existing `HasIndex((MemberId, WorkItemId, CheckpointKey))` if it's unique; add a partial unique index keyed by `(MemberId, WorkItemId, CheckpointKey, COALESCE(task_id, '<root>'))` over rows where `dismissed_at IS NULL`. Use a raw SQL `CREATE UNIQUE INDEX` in the migration if EF can't model the COALESCE-with-filter combo cleanly.
- [ ] `dotnet ef database update` applies cleanly to a clean DB. Full backend test suite (currently 182) still passes — Testcontainers exercises every migration.
- [ ] `docs/data-model.md` updated: CheckpointContract, WorkItem, PendingActionSignal entity tables + changelog row.

**Files to Modify/Create:**
- Modify: `src/DevHub.Modules.ExecutorRegistry/Entities/CheckpointContract.cs`
- Modify: `src/DevHub.Modules.WorkItems/Entities/WorkItem.cs`
- Modify: `src/DevHub.Modules.Notifications/Entities/PendingActionSignal.cs`
- Modify: corresponding DbContext model-builder calls
- Create: 3 migration files (one per module)
- Modify: `docs/data-model.md`

**Technical Notes:**
The COALESCE-filtered unique index is the only EF-awkward bit. If `HasIndex(...).IsUnique().HasFilter(...)` won't accept the expression, drop down to `migrationBuilder.Sql("CREATE UNIQUE INDEX ...")` in the migration body. The C# model-builder index can be non-unique; the unique constraint lives in the migration SQL. Document that in a comment near the DbContext config.

---

## Backend

### T-065: Executor response shape + WorkItem.CurrentTaskId thread-through

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-064

**Description:**
`ExecutorStartResponse`, `ExecutorFetchResponse`, and `ExecutorSignalResponse` gain optional `CurrentTaskId`. `WorkItemsService.StartAsync` / `GetAsync` and `CheckpointSignalsService.SignalAsync` write the value to `WorkItem.CurrentTaskId` on every transition (same opportunistic-cache pattern as `CurrentStatus` / `CurrentCheckpointKey` today).

**Rationale:**
AC-1, AC-2. Per-task pending-action identity depends on the work item knowing what task it's currently on; the executor is authoritative.

**Acceptance Criteria:**
- [ ] Three executor-response records in `DevHub.Contracts.Executors` gain `string? CurrentTaskId`. Defaults to `null` on absent JSON field.
- [ ] `WorkItemDto` + `WorkItemSummaryDto` carry `CurrentTaskId`.
- [ ] On every transition where DevHub's cache differs from the executor's response, `CurrentTaskId` is updated alongside `CurrentStatus` / `CurrentCheckpointKey`.
- [ ] `IWorkItemLookup` (the cross-module shape consumed by the reconciler) exposes `CurrentTaskId` on its lookup result.
- [ ] Backwards-compatible: existing FakeExecutor responses (which don't include `currentTaskId`) continue to deserialize without errors; `WorkItem.CurrentTaskId` stays `null`.
- [ ] `dotnet test` 182/182 still green; FakeExecutor smoke tests untouched.
- [ ] `docs/api-spec.md` WorkItem DTO section + changelog.

**Files to Modify/Create:**
- Modify: `src/DevHub.Contracts/Executors/ExecutorStartResponse.cs`, `ExecutorFetchResponse.cs`, `ExecutorSignalResponse.cs`
- Modify: `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs`
- Modify: `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` + `CheckpointSignalsService.cs`
- Modify: `src/DevHub.Contracts/WorkItems/IWorkItemLookup.cs` + lookup implementation
- Modify: `docs/api-spec.md`

**Technical Notes:**
Mirrors the System.Text.Json pattern used for `intake.codeSource` in T-059 — `JsonPropertyName("currentTaskId")` + nullable C# type. Don't tie this to `perTask=true` at the executor-response level; the executor sends `currentTaskId` whenever it has one, regardless of contract metadata. The reconciler decides when it matters.

---

### T-066: CheckpointContract.PerTask flag on registration + DTO

**Type:** Backend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-064

**Description:**
Extend `ReplaceContractsRequest.CheckpointContractInput` (the inner record posted by `POST /api/admin/executors/{id}/checkpoint-contracts`) and `CheckpointContractDto` with an optional `PerTask` boolean. Persist on `CheckpointContract`. No validation guards — `perTask` is an opt-in flag; defaults to `false`.

**Rationale:**
AC-1. The contract registry has to surface the new flag for the reconciler to key per-task.

**Acceptance Criteria:**
- [ ] `ReplaceContractsRequest` accepts `perTask: bool` (optional, defaults `false`).
- [ ] `CheckpointContractDto` carries `perTask`.
- [ ] Admin GET `/api/admin/executors/{id}` round-trips the flag.
- [ ] `IExecutorRouter.GetCheckpointContractAsync` returns the flag in its descriptor (the reconciler reads it from there).
- [ ] `docs/api-spec.md` ExecutorRegistry section + changelog.
- [ ] `docs/data-model.md` already updated in T-064 — verify the field description includes "When true, pending actions are keyed per task" pointer.

**Files to Modify/Create:**
- Modify: `src/DevHub.Modules.ExecutorRegistry/DTOs/*.cs` (whichever holds `ReplaceContractsRequest` + `CheckpointContractDto`)
- Modify: `src/DevHub.Modules.ExecutorRegistry/Services/*.cs` (persistence + projection)
- Modify: `src/DevHub.Contracts/Executors/*.cs` (the cross-module descriptor that `IExecutorRouter` returns)
- Modify: `docs/api-spec.md`

**Technical Notes:**
Replace semantics on the contract list (per FEAT-003's design: `POST .../checkpoint-contracts` atomically replaces the whole set) means the operator must include `perTask` on every contract they want it on — there's no per-key PATCH. Document that in the request body example.

---

### T-067: PendingActionReconciler — per-task identity + DTO/event extension

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-064, T-065, T-066

**Description:**
The reconciler reads `WorkItem.CurrentTaskId` from the lookup and the active `CheckpointContract.PerTask` flag from the router. When `perTask=true`, it keys pending rows by `(MemberId, WorkItemId, CheckpointKey, TaskId)` and writes the task id to new rows. When `perTask=false`, behavior is byte-for-byte identical to today (TaskId stays `null`). `PendingActionEvent` + `PendingActionDto` gain nullable `TaskId`.

**Rationale:**
AC-2, AC-3, AC-8. The core data-flow change. Loop-back (T-001 closes → T-002 opens) works because the discriminator is `task_id`, not `checkpoint_key`.

**Acceptance Criteria:**
- [ ] `PendingActionEvent` and `PendingActionDto` carry `string? TaskId`. SSE event JSON includes `taskId` when set.
- [ ] When the active contract has `perTask=true`:
  - Rows for required members are raised with `TaskId = wi.CurrentTaskId`.
  - Existing-row lookup uses `(MemberId, WorkItemId, CheckpointKey, TaskId)`.
  - Closing T-001 (status moves past it) does NOT block opening T-002 — the loop-back test passes.
- [ ] When the active contract has `perTask=false`: no behavior change, `TaskId` stays `null` on raised rows, all existing tests pass.
- [ ] `GET /api/notifications/pending` round-trips `taskId`.
- [ ] `docs/api-spec.md` Notifications section + changelog.

**Files to Modify/Create:**
- Modify: `src/DevHub.Modules.Notifications/Services/PendingActionReconciler.cs`
- Modify: `src/DevHub.Modules.Notifications/Services/NotificationsQueryService.cs` (DTO projection)
- Modify: `src/DevHub.Modules.Notifications/Entities/PendingActionSignal.cs` (already done in T-064)
- Modify: `src/DevHub.Contracts/Notifications/PendingActionEvent.cs`
- Modify: `src/DevHub.Modules.Notifications/DTOs/PendingActionDto.cs`
- Modify: `docs/api-spec.md`

**Technical Notes:**
The reconciler today fetches contract metadata via `router.GetCheckpointContractAsync`. T-066's descriptor extension surfaces `perTask` there. No new round-trip needed.

The "stale rows" pass (rows whose `CheckpointKey` no longer matches the work item's current checkpoint) is unchanged — but with per-task pauses, the loop-back case is "same checkpoint key, different task id." That looks identical to the "active key" path with a new task id, not the "stale" path. The discriminator-based lookup handles it naturally; no new branch needed.

---

### T-068: Signal endpoint forwards `taskId` + validates `payload.assignee` non-empty

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-066

**Description:**
`SignalRequest` DTO gains optional `taskId`. `CheckpointSignalsService.SignalAsync` forwards it verbatim to the executor's signal endpoint. Boundary validation: when the active contract has `perTask=true` AND `checkpointKey="assignment-confirmed"`, require `payload.assignee` to be a non-empty string. Audit details carry `taskId` and `assignee`.

**Rationale:**
AC-4, AC-5, AC-10. The end-user-facing path: the operator submits a signal, DevHub validates + forwards, the executor's idempotency key (`(run_id, checkpoint, task_id)`) takes over.

**Acceptance Criteria:**
- [ ] `SignalRequest` (in `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs`) gains optional `string? TaskId`.
- [ ] `ExecutorHttpClient.SignalAsync` includes `taskId` in the outbound JSON body when set (omitted, not `null`, when unset — matches the orchestrator's omit-don't-null pattern).
- [ ] When `payload.assignee` is missing or empty AND the contract is `perTask=true` + `assignment-confirmed`: return 400 with a `ValidationException` problem-detail tagged `payload.assignee`. No executor call attempted.
- [ ] Other checkpoints don't run the assignee guard.
- [ ] Audit details include `taskId` and `assignee` (when present) on the `workitem:signal` row.
- [ ] `docs/api-spec.md` Signal endpoint section + changelog.

**Files to Modify/Create:**
- Modify: `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs` (SignalRequest)
- Modify: `src/DevHub.Modules.WorkItems/Services/CheckpointSignalsService.cs`
- Modify: `src/DevHub.Modules.WorkItems/Services/ExecutorHttpClient.cs`
- Modify: `src/DevHub.Contracts/Executors/IExecutorHttpClient.cs`
- Modify: `docs/api-spec.md`

**Technical Notes:**
DevHub does not enforce that `assignee` corresponds to a real DevHub member — the field is an opaque string from the executor's perspective. The UI's member-picker is convenience; the free-text escape hatch is intentional. Validation is shape-only: non-empty string.

The assignee guard is the only place in DevHub that special-cases the `assignment-confirmed` checkpoint key. Document why: it's the only contract today whose `payload` shape is required to be non-empty. Other contracts treat `payload` as optional.

---

## Frontend

### T-069: SPA types + service mirrors for per-task fields

**Type:** Frontend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-066, T-067, T-068

**Description:**
Mirror the backend additions in `client/src/app/core/api`. `PendingActionDto.taskId`, `SignalRequest.taskId`, `CheckpointContractView.perTask`. No new service methods needed — the existing `WorkItemsService.signal(...)` accepts the extended body, the existing `NotificationsService.list*(...)` returns the extended DTO.

**Rationale:**
Wire-level parity. T-070 / T-071 / T-072 need these types.

**Acceptance Criteria:**
- [ ] `client/src/app/core/api/notifications.types.ts` extends `PendingActionDto` with `taskId?: string | null`.
- [ ] `client/src/app/core/api/work-items.types.ts` extends `SignalRequest` with `taskId?: string`.
- [ ] `client/src/app/core/api/executor-registry.types.ts` extends `CheckpointContractView` with `perTask: boolean` (defaulting to `false` on the wire — backend always sends).
- [ ] Service spec coverage: at least one existing spec touches a fixture that now includes `taskId: null` to confirm the type widening is non-breaking.

**Files to Modify/Create:**
- Modify: `client/src/app/core/api/notifications.types.ts`
- Modify: `client/src/app/core/api/work-items.types.ts`
- Modify: `client/src/app/core/api/executor-registry.types.ts`

**Technical Notes:**
The pattern is identical to T-060 (FEAT-008). Type widening, no service-code changes.

---

### T-070: AssignmentConfirmPanel — member-picker + free-text fallback

**Type:** Frontend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-069

**Description:**
A new `AssignmentConfirmPanel` component on the lifecycle review page (`review.page`). The page already routes by `checkpointKey`; when the active contract is `perTask=true` AND `checkpointKey="assignment-confirmed"`, swap in this panel instead of `CheckpointActionBar`. Renders a member-picker scoped to the project's memberships (via `WorkspaceService.listMemberships`) plus a "Other (free text)" toggle that reveals a text input. Submit emits `{ outcome: "confirmed", payload: { assignee: <value>, taskId: <current> } }`.

**Rationale:**
AC-4. The operator-facing input form. Workflow is `standard` because the surrounding screen (review page) is unchanged in layout; we're adding a panel variant, not redesigning.

**Acceptance Criteria:**
- [ ] Review page selects between `CheckpointActionBar` and `AssignmentConfirmPanel` by inspecting the active contract's `perTask` + `checkpointKey`.
- [ ] Member-picker lists the project's memberships by display name; selecting one populates the assignee value as the display name (configurable later if email is preferred — v1 picks displayName).
- [ ] Free-text mode reveals a single input (no validation beyond non-empty for the submit button to enable).
- [ ] Submit button is disabled until a value (picked or typed) is non-empty.
- [ ] On successful submit, the panel closes and the page refreshes (existing handler — same path as `CheckpointActionBar`'s submit).
- [ ] At least 3 specs: picker mode submit, free-text mode submit, empty-value submit disabled.

**Files to Modify/Create:**
- Create: `client/src/app/features/projects/work-items/components/assignment-confirm-panel.ts` + `.html` + `.spec.ts`
- Modify: `client/src/app/features/projects/work-items/review.page.ts` + `.html` (swap logic)

**Technical Notes:**
The page already loads the contract via `getCheckpoint(...)`. T-069's type widening means `contract.perTask` is in scope here. Memberships need a one-shot fetch (`listMemberships(projectId)`) — cache in a signal for the panel's lifetime.

The assignee value is intentionally string-only on the wire. Don't normalize email vs displayName — let the operator's free-text escape hatch handle the long tail.

---

### T-071: Per-task labeling on pending-action rows (operator dashboard + home)

**Type:** Frontend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-069

**Description:**
The `pending-action-list` component (used by operator dashboard + home page) renders per-task pending rows distinctly. When `pendingAction.taskId` is set, the row label becomes `<work item title> — <task id>`. Clicking the row routes to the review page with `?taskId=<id>` so the page can pre-filter to the right task (the executor's `current_task_id` should already match, but the URL param makes the deep-link explicit).

**Rationale:**
AC-6, AC-7. Without this, multi-task work items show ambiguous rows.

**Acceptance Criteria:**
- [ ] Row label includes `— <task id>` suffix when `taskId` is non-null. Plain title when null.
- [ ] Click handler routes to `/projects/{slug}/work-items/{id}/review?taskId={taskId}` when set; existing route without the query param otherwise.
- [ ] Spec: a list with two rows for the same work item (different task ids) renders distinct labels and each click navigates with the right query param.
- [ ] No badge logic change — `PendingActionsStore.count` already counts list length.

**Files to Modify/Create:**
- Modify: `client/src/app/features/home/pending-action-list.ts` + `.html`
- Modify: `client/src/app/features/home/pending-action-list.spec.ts`
- Modify: `client/src/app/features/operator/operator-dashboard.page.ts` + `.html` (if the list isn't reused — verify and apply minimal edits)

**Technical Notes:**
Pending-action-list is shared across two surfaces; touching it once covers both. The `?taskId` query param is informational on the review page side; T-070's panel uses the executor's `currentTaskId` from the work item, not the URL. The URL param is just for human-readability and back-button friendliness.

---

### T-072: WorkItem detail Assignments sidebar

**Type:** Frontend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-069

**Description:**
On `work-item-detail.page`, add an "Assignments" sidebar below the existing Branch row. Reads `workItem.executorState.assignments` (a `Record<taskId, assignee>` map written by the orchestrator's `assignment-confirmed` handler per IMP-005). Renders one row per `taskId → assignee` pair. No DevHub-side persistence — purely read-through.

**Rationale:**
AC-9. Surface the orchestrator's `RunMemory.data.assignments` sidecar without persisting it in DevHub.

**Acceptance Criteria:**
- [ ] Sidebar renders only when `executorState.assignments` is a non-empty object.
- [ ] Empty / missing sidecar → no sidebar row (no "(none)" placeholder; the absence is the signal).
- [ ] One row per assignment: `T-001 → Alice` style. `taskId` rendered monospace; assignee plain text.
- [ ] Order: deterministic — sort by `taskId` ascending (executor's IDs are usually like `T-001`, `T-002`).
- [ ] Spec: assignments object with two entries renders both, sorted; empty/missing renders nothing.
- [ ] `docs/ui-specification.md` updated (WorkItemDetailPage section) + changelog.

**Files to Modify/Create:**
- Modify: `client/src/app/features/projects/work-items/work-item-detail.page.ts` + `.html` + `.spec.ts`
- Modify: `docs/ui-specification.md`

**Technical Notes:**
`executorState` is typed as `unknown` today — the page already treats it as opaque. The Assignments reader does a narrow shape check (`typeof === 'object'` + key iteration) and falls back to "no sidebar" on any malformed shape. No new wire type needed; this is intentionally a soft read.

---

## Testing

### T-073: Integration tests — per-task lifecycle, loop-back, signal, audit

**Type:** Testing · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-064, T-067, T-068

**Description:**
End-to-end fixture tests covering the per-task lifecycle:
1. Pending-action uniqueness: T-001 raised, T-002 also raised for same work item / checkpoint — both rows coexist.
2. Loop-back: T-001 closes (executor moves the work item past it), T-002 opens — T-001's row stays closed and is NOT re-opened.
3. Signal forward: `POST /signal` with `{ outcome, taskId, payload: { assignee } }` reaches the FakeExecutor with both fields in the body.
4. Validation: assignment-confirmed without `payload.assignee` returns 400 at the DevHub boundary; no executor call.
5. Audit: `workitem:signal` row carries `taskId` + `assignee` in details.
6. Backward compatibility: a contract with `perTask=false` reconciles exactly as today (existing acceptance tests still green).

**Rationale:**
The brief's quality bar. Closes AC-2 / AC-3 / AC-4 / AC-5 / AC-8 / AC-10 in fixture form.

**Acceptance Criteria:**
- [ ] New file `tests/DevHub.Modules.Notifications.Tests/Acceptance/PerTaskPendingActionTests.cs` covers points 1, 2, and 6.
- [ ] New file `tests/DevHub.Modules.WorkItems.Tests/Acceptance/AssignmentSignalTests.cs` covers points 3, 4, 5.
- [ ] FakeExecutor extended (if needed) to drive a multi-task script: emit `currentTaskId=T-001` on start, advance via signal to `T-002`, etc. Reuse the recording helper from T-063.
- [ ] All existing 182 backend tests stay green.

**Files to Modify/Create:**
- Create: `tests/DevHub.Modules.Notifications.Tests/Acceptance/PerTaskPendingActionTests.cs`
- Create: `tests/DevHub.Modules.WorkItems.Tests/Acceptance/AssignmentSignalTests.cs`
- Modify (if needed): `tests/DevHub.TestHarness/FakeExecutor/FakeExecutorHost.cs` (multi-task scripting)

**Technical Notes:**
The FakeExecutor's scripted-response model already supports per-call status / checkpoint mutation. The per-task lifecycle just needs a "current task" sequence the harness advances by signal. Simplest: extend `Scripted` with `CurrentTaskId` (single mutable field, set by tests). On `assignment-confirmed` signal received, the test advances the script (e.g. T-001 → T-002).

---

## Summary

| Type | Count |
|------|-------|
| Database | 1 (T-064) |
| Backend | 4 (T-065, T-066, T-067, T-068) |
| Frontend | 4 (T-069, T-070, T-071, T-072) |
| Testing | 1 (T-073) |
| **Total** | **10** |

| Complexity | Count |
|------------|-------|
| S | 4 (T-064, T-066, T-069, T-071, T-072) |
| M | 5 (T-065, T-067, T-068, T-070, T-073) |
| L | 0 |

(Counts off by one in the totals — T-072 is S; corrected: S=5, M=5.)

**Critical path:** T-064 → T-065 + T-066 → T-067 → T-068 → T-073. ~6 sequential tasks on the deadline-driver chain. T-069 unblocks T-070 / T-071 / T-072 in parallel.

**Dependency DAG:**
```
T-064 ──┬──→ T-065 ──┐
        │           ├──→ T-067 ──→ T-073
        └──→ T-066 ──┤              ↑
                     └──→ T-068 ────┘
                          │
                          └──→ T-069 ──┬──→ T-070
                                       ├──→ T-071
                                       └──→ T-072
```

**Risks / open questions:**

- **EF + `COALESCE` unique index.** The C# model-builder may not express the partial-with-COALESCE index cleanly. Plan: drop down to raw SQL in the migration body. T-064's plan should call this out explicitly.
- **WorkItem.CurrentTaskId staleness.** The cache is updated on every transition we observe (start, signal, get). Between transitions, the value can be stale. The reconciler runs after every transition via `RecomputeForWorkItemAsync`, so the staleness window is bounded by the transition-to-reconcile latency — same window we already accept for `CurrentCheckpointKey`.
- **Member-picker for free-text drift.** v1 picks `displayName` as the assignee string. If the operator types a different value via free text and the team later renames the member, the older audit rows show the stale displayName. Acceptable for v1 — audit is a record of what happened, not what is. Document in the panel's spec.
- **FakeExecutor multi-task scripting** is new ground for the test harness. T-073 may discover that some assumptions in the existing FakeExecutorHost (single Scripted profile per fake) need a small extension. Budget for one minor harness change.
- **No DevHub-side dedupe.** The executor's idempotency hash on `(run_id, "assignment-confirmed", task_id)` covers replay. We must not add a DevHub-side dedupe layer — this risk is mostly about future maintainers; the brief's §10 calls it out.
