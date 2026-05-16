# FEAT-004 Task Breakdown — Work Items Façade

> Generated from `docs/work-items/FEAT-004-work-items-facade.md` using `.ai-framework/prompts/feature-tasks.md`. 8 tasks across Backend, Testing, and Frontend.

## Scope choices locked in before generation

- **Executor HTTP boundary:** new `IExecutorHttpClient` published from `DevHub.Contracts` with one method per executor verb (`StartAsync`, `FetchStateAsync`, `SignalAsync`, `OpenStreamAsync`, `CancelAsync`). Production impl is `Refit`-free, hand-rolled `HttpClient` over the executor's base URL with `Authorization: Bearer <resolvedCredentials>`; the credentials come from `IExecutorCredentialResolver` and never live longer than the request.
- **Fake executor for tests:** an in-test `Microsoft.AspNetCore` host bound to a random port, registered in `DevHubApiFactory` opt-in. Lets us verify AC-1 (no outbound HTTP on deny — assert call counter is zero) and AC-4 (SSE chunks arrive byte-for-byte) without mocking.
- **`executorCorrelationMarker`:** generated client-side in DevHub before the start call (`Guid.NewGuid().ToString("N")`). Stored on `WorkItem` and passed as a header (`X-DevHub-Correlation`) on every forward.
- **Audit columns:** every Granted, Denied, and Failed signal/start/cancel writes an `AuditEntry` with `target_type="WorkItem"` (or `"CheckpointSignal"` for signal rows). Details include `executorCorrelationMarker`, `executorKey`, `executorResponseStatus` (when present), `correlationId` (for 502 paths).
- **Idempotency on signals:** client supplies `Idempotency-Key` HTTP header; server stores `(work_item_id, idempotency_key)` unique. Repeating the same `(workItem, key)` returns the original 200 response within 24h; missing/different keys do not deduplicate.
- **SSE pass-through:** controller writes directly to `HttpContext.Response.Body` with `Response.Headers.Append("Content-Type", "text/event-stream")`, `Response.Headers.Append("X-Accel-Buffering", "no")`, and a `using` over the upstream `HttpClient` stream opened with `HttpCompletionOption.ResponseHeadersRead`. Authorize ONCE at connection time; on auth deny we never open the upstream socket.
- **Status cache refresh policy:** `current_status` and `current_checkpoint_key` on `WorkItem` are updated opportunistically — every successful `FetchStateAsync` overwrites them. We do not background-poll; FEAT-005's notification recomputation reads the cache.
- **No cross-project work items.** Hard rule from stakeholder definition.

---

## Backend

### T-034: WorkItems foundation — entities + DbContext + real migration

**Type:** Backend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-009 (stub migration), T-022 (audit), T-029 (registry foundation)

**Description:**
Replace T-009's empty WorkItems migration with the real one. Land `WorkItem` and `CheckpointSignal` per `docs/data-model.md` §247–293, wire `WorkItemsDbContext` with the per-row indexes and `(executor_id, executor_correlation_marker)` unique constraint, plus the optional `(work_item_id, idempotency_key)` unique for signal deduplication.

**Rationale:**
Every other FEAT-004 task touches at least one of these entities or their columns.

**Acceptance Criteria:**
- [ ] `dotnet ef database update --project src/DevHub.Modules.WorkItems` creates `work_items.work_items` and `work_items.checkpoint_signals` with all columns from data-model.md.
- [ ] Unique on `(executor_id, executor_correlation_marker)`.
- [ ] Index on `(project_id, current_status)`.
- [ ] Index on `(work_item_id, signaled_at desc)` for `CheckpointSignal`.
- [ ] `CheckpointSignal.IdempotencyKey` (new, nullable varchar(60)) + unique `(work_item_id, idempotency_key)` where `idempotency_key IS NOT NULL`.
- [ ] No nav properties to entities in other modules — FK columns only.

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.WorkItems/Entities/WorkItem.cs`, `CheckpointSignal.cs`
- Modify: `src/DevHub.Modules.WorkItems/WorkItemsDbContext.cs` (DbSets, fluent config, indexes)
- Replace: `src/DevHub.Modules.WorkItems/Migrations/*` (real `Initial`)

**Technical Notes:**
`WorkItem.CurrentStatus` stays as `varchar(60)` since executors define their own statuses (`Running`, `WaitingOnCheckpoint`, `Completed`, `Failed`, `Cancelled`). No CLR enum — keep the API string-typed. `WorkItem` is `BaseEntity` only; it's never soft-deleted (cancellation is a status, per business rule).

---

### T-035: Executor HTTP client + fake executor for tests

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-029 (executor registry + `IExecutorCredentialResolver`)

**Description:**
Publish the `IExecutorHttpClient` contract from `DevHub.Contracts`, ship the production implementation (`HttpClient`-backed, reads the base URL from the resolved `ExecutorRegistrationDescriptor` and the bearer token from `IExecutorCredentialResolver`), and ship a Kestrel-hosted fake executor for integration tests that records every call and lets tests drive both happy-path JSON responses and controlled SSE byte sequences.

**Rationale:**
- AC-1 ("denied actions never reach the executor") requires asserting **absence** of outbound HTTP — only a real fake executor with a call counter can prove that.
- AC-4 ("SSE bytes arrive without buffering") requires a real upstream that emits known chunks at known intervals.
- A real abstraction makes FEAT-005's notification refresh cleaner too (it can reuse `FetchStateAsync`).

**Acceptance Criteria:**
- [ ] `IExecutorHttpClient` has: `StartAsync(executor, body, correlationMarker, ct)`, `FetchStateAsync(executor, correlationMarker, ct)`, `SignalAsync(executor, correlationMarker, checkpointKey, body, ct)`, `OpenStreamAsync(executor, correlationMarker, ct)` (returns a readable `Stream`), `CancelAsync(executor, correlationMarker, ct)`.
- [ ] Production impl adds `Authorization: Bearer <env>` per call. The resolved value is read transiently from `IExecutorCredentialResolver` and never stored as a field.
- [ ] 502 mapping: any non-2xx response (or connection failure) throws `ExecutorFailureException` with `executorId`, `executorKey`, `correlationId` (server-side `Guid.NewGuid().ToString("N")`), and the executor's own response body under `details`.
- [ ] `tests/DevHub.TestHarness/FakeExecutor` boots on a random localhost port, returns scriptable JSON for start/fetch/signal/cancel, streams scriptable bytes for `OpenStreamAsync`, and exposes a `Calls` property for assertions.
- [ ] `DevHubApiFactory.WithFakeExecutor()` opt-in registers the fake's port + a seeded `ExecutorRegistration` + `ExecutorBinding` pointing at it.

**Files to Modify/Create:**
- Create: `src/DevHub.Contracts/Executors/IExecutorHttpClient.cs` and request/response record types
- Create: `src/DevHub.Contracts/ApplicationErrors/ExecutorFailureException.cs` (subclass of `DomainException`, 502)
- Create: `src/DevHub.Modules.WorkItems/Services/ExecutorHttpClient.cs` (typed `HttpClient` via `IHttpClientFactory`)
- Modify: `src/DevHub.Modules.WorkItems/WorkItemsModuleExtensions.cs` (register `AddHttpClient<IExecutorHttpClient, ExecutorHttpClient>()`)
- Modify: `src/DevHub.Api/Middleware/ProblemDetailsMiddleware.cs` (translate `ExecutorFailureException` to `/probs/executor-failure` 502)
- Create: `tests/DevHub.TestHarness/FakeExecutor/FakeExecutorHost.cs`, `ScriptedResponse.cs`, `CallRecord.cs`
- Modify: `tests/DevHub.TestHarness/DevHubApiFactory.cs` (add `WithFakeExecutor()` knob)

**Technical Notes:**
The fake executor is a `WebApplication.CreateBuilder().Build()` started on `http://127.0.0.1:0` so the OS picks the port; the seeded `ExecutorRegistration.BaseUrl` is filled with that port. SSE endpoint in the fake writes `data: ...\n\n` chunks with `await Response.Body.WriteAsync(...)` and `await Response.Body.FlushAsync()` between chunks. The test driver sets `Calls` on every entry; tests assert `fake.Calls.Count == 0` for AC-1 deny.

---

### T-036: Backend services + controllers (start, get, signal, signals, cancel)

**Type:** Backend · **Workflow:** standard · **Complexity:** XL · **Dependencies:** T-034, T-035

**Description:**
Land the five JSON façade endpoints under `/api/projects/{projectId}/work-items/*`. Every action:
1. Authorize `(member, role, project, action)` via `IProjectAuthorizationService` — for signal/cancel the `requiredRoleKey` comes from `IExecutorRouter.GetCheckpointContractAsync(...)`. Operators get the workspace-wide grant.
2. Open a `WorkItemsDbContext` transaction.
3. Validate (outcome in `allowedOutcomes`, work item exists, checkpoint key exists on contract, idempotency-key check).
4. Capture an outbound `CheckpointSignal` row (for signal/cancel) BEFORE forwarding.
5. Forward via `IExecutorHttpClient`. On success update `current_status` / `current_checkpoint_key` / `executor_response_status`; on failure raise `ExecutorFailureException`.
6. Audit `Granted` (or `Failed` on 502) inside the same transaction.

**Rationale:**
This is what DevHub exists for. AC-1/3/5/6 all land here.

**Acceptance Criteria:**
- [ ] `POST /api/projects/{id}/work-items` — authorize as the start-role for the executor (resolved from `CheckpointContract` whose `checkpointKey == "start"`; if absent, defaults to `operator`). Generates `executorCorrelationMarker`, forwards `StartAsync`, inserts the `WorkItem`, audits Granted with the marker.
- [ ] `GET /api/projects/{id}/work-items/{wid}` — project:any. Fetches latest from executor; refreshes the status cache opportunistically; returns `WorkItemDto` with opaque `executorState`.
- [ ] `GET /api/projects/{id}/work-items` — paginated, supports `status`, `waitingOnMe` filters; reads the index cache only (no per-row executor calls).
- [ ] `POST /.../checkpoints/{key}/signal` — authorize against the contract's `requiredRoleKey`; reject with 400 if `outcome` ∉ `allowedOutcomes`; idempotency via `Idempotency-Key` header within 24h.
- [ ] `POST /.../cancel` — authorize against the cancel contract's `requiredRoleKey` (fallback: `operator` if no cancel contract).
- [ ] `GET /.../signals` — last N (default 20), paginated; project:any.
- [ ] 502 path writes a `Failed` audit row with `executorResponseStatus` + `correlationId` in `Details`. The `CheckpointSignal` row stays with `executor_response_status = null` (FEAT-006 will surface "stuck signals").
- [ ] Every Granted audit row carries `executorCorrelationMarker` (AC-3).

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.WorkItems/DTOs/*.cs` (WorkItemSummaryDto, WorkItemDto, CheckpointSignalDto, StartWorkItemRequest, SignalRequest, paginated wrappers)
- Create: `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs`, `CheckpointSignalsService.cs`
- Create: `src/DevHub.Modules.WorkItems/Controllers/WorkItemsController.cs`, `CheckpointSignalsController.cs`
- Modify: `src/DevHub.Modules.WorkItems/WorkItemsModuleExtensions.cs`
- Verify: `src/DevHub.Api/Program.cs` `AddApplicationPart(typeof(WorkItemsDbContext).Assembly)` still picks both controllers up.

**Technical Notes:**
The "start role" is contract-driven: if any `CheckpointContract` for the executor has `checkpointKey == "start"`, use its `requiredRoleKey`; otherwise default to `operator`. Document the convention on `CheckpointContract` so executor authors know to ship a `start` contract if they want a per-project starter role.

Authorization for signal/cancel: resolve the contract by `(executorId, checkpointKey)` via `IExecutorRouter.GetCheckpointContractAsync` BEFORE the auth call; if the contract is missing return 404 (the executor doesn't define this checkpoint). Then `EnsureAuthorizedAsync(projectId, action, requiredRoleKey)` — the existing audit path covers grant/deny.

Status-cache write: after `FetchStateAsync` and after a signal forwards successfully, `db.WorkItems.Where(w => w.Id == id).ExecuteUpdateAsync(...)` to bump `current_status` / `current_checkpoint_key`. Done outside the audit transaction (post-commit) so a slow executor response doesn't widen our locks.

---

### T-037: SSE stream pass-through endpoint

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-036

**Description:**
Land `GET /api/projects/{id}/work-items/{wid}/stream` with byte-for-byte pass-through. Authorize once at connection time (project:any). Open the upstream stream via `IExecutorHttpClient.OpenStreamAsync(...)` with `HttpCompletionOption.ResponseHeadersRead`, set the response headers, and copy chunks from the upstream `Stream` to `HttpContext.Response.Body` without buffering. Disconnects propagate via `HttpContext.RequestAborted`.

**Rationale:**
AC-4 says the streaming hot path must feel native. The whole DevHub value claim ("live state passes through") collapses if we buffer.

**Acceptance Criteria:**
- [ ] On auth deny, the upstream HTTP socket is **never opened** (assert `fake.Calls.OpenStream == 0`).
- [ ] On auth grant, `Content-Type: text/event-stream`, `Cache-Control: no-store`, `X-Accel-Buffering: no` are set; no `Content-Length` header.
- [ ] Bytes written by the fake executor arrive at the test client chunk-by-chunk in the same shape (verified by reading the response stream and asserting per-chunk timestamps differ by ≥ the fake's inter-chunk delay).
- [ ] Client disconnect closes the upstream stream within 1s (verified by `HttpContext.RequestAborted` propagating + `CancellationToken` cancelling the inner `CopyToAsync`).
- [ ] Audit row written on connection grant (one row per stream open, NOT per chunk).

**Files to Modify/Create:**
- Modify: `src/DevHub.Modules.WorkItems/Controllers/WorkItemsController.cs` (add `[HttpGet("stream")]` action)
- Create: `src/DevHub.Modules.WorkItems/Services/WorkItemStreamForwarder.cs` (encapsulates the open-and-copy loop with cancellation)

**Technical Notes:**
The action returns `EmptyResult` after writing — do not return any IActionResult that triggers MVC formatters; that's where buffering creeps in. Use `Response.Body.WriteAsync` directly in the forwarder. Set headers BEFORE the first byte (`Response.StartAsync()` is implicit on first write).

Cancellation: pass `HttpContext.RequestAborted` into both `OpenStreamAsync` and the inner `CopyToAsync`. The `using` on the upstream `HttpResponseMessage` + `Stream` handles disposal.

---

### T-038: Integration tests — endpoints + stream + AC verification

**Type:** Testing · **Workflow:** standard · **Complexity:** L · **Dependencies:** T-035, T-036, T-037

**Description:**
Per-controller `*EndpointsTests` for grant + deny on every mutation; `WorkItemStreamTests` for SSE pass-through behavior; AC verification suite (`FacadeAcceptanceTests`) for AC-1, AC-4, AC-6.

**Rationale:**
FEAT-001 set the discipline. Five mutation endpoints + the stream + the cross-cutting ACs all need explicit coverage.

**Acceptance Criteria:**
- [ ] `WorkItemsEndpointsTests`: start (grant + deny + 409 on no binding), get (any-role), list (paginated), cancel (role-gated + operator override).
- [ ] `CheckpointSignalsEndpointsTests`: signal grant + deny + 400 (outcome not in allowedOutcomes) + 404 (unknown checkpoint key) + 409 (already-resolved checkpoint) + idempotency replay.
- [ ] `WorkItemStreamTests`: opens stream after auth, byte-for-byte chunks, client disconnect closes upstream, deny path **never opens upstream socket** (`fake.Calls.OpenStream == 0`).
- [ ] `FacadeAcceptanceTests` (cross-cutting): AC-1 (deny → zero outbound calls), AC-3 (Granted audit carries `executorCorrelationMarker`), AC-6 (invalid outcome 400 before forward; assert `fake.Calls.Signal == 0`).
- [ ] AC-2 (latency) covered by a soft assertion: DevHub-mediated `GET /work-items/{id}` round-trip is < 5× the fake's direct response time on the same loopback.

**Files to Modify/Create:**
- Create: `tests/DevHub.Modules.WorkItems.Tests/WorkItemsEndpointsTests.cs`
- Create: `tests/DevHub.Modules.WorkItems.Tests/CheckpointSignalsEndpointsTests.cs`
- Create: `tests/DevHub.Modules.WorkItems.Tests/WorkItemStreamTests.cs`
- Create: `tests/DevHub.Modules.WorkItems.Tests/FacadeAcceptanceTests.cs`
- Create: `tests/DevHub.Modules.WorkItems.Tests/Helpers/WorkItemsTestHelpers.cs` (login operator + fresh member, start helper, signal helper, `fake.Calls.*` assertions)
- Modify: `tests/DevHub.Modules.WorkItems.Tests/DevHub.Modules.WorkItems.Tests.csproj` (add Audit + Identity + Workspace + WorkItems refs)

**Technical Notes:**
The `WithFakeExecutor()` factory knob from T-035 is the workhorse — every test class opts in. AC-1 deny is asserted by attempting the call as a fresh non-operator with no project membership; `fake.Calls.Total` must be `0` after the 403 response. The stream chunk-arrival test reads the response stream with a `StreamReader`, records `Stopwatch.GetTimestamp()` per chunk, and asserts the deltas match the fake's `await Task.Delay(50)` between writes within a tolerance.

---

## Frontend

### T-039: Work-items service + Project home table + StartWorkModal

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M · **Dependencies:** T-036, T-026 (shared components)

**Description:**
Replace the FEAT-002 "Work items land in FEAT-004" placeholder on Project home with a real `WorkItemTable` driven by `GET /api/projects/{id}/work-items`. Add a "Start work" button that opens `StartWorkModal` (opaque JSON input — the contract is executor-shaped). Filters: status pill bar + "Waiting on me" toggle.

**Rationale:**
First place a project member sees a working DevHub. UI Spec §4.

**Acceptance Criteria:**
- [ ] Mockup at `mockups/project-home-with-workitems.html` extending the existing `project-home.html` shell.
- [ ] `WorkItemsService` (sibling to `WorkspaceService`/`ExecutorRegistryService`) ships typed wrappers for: `list`, `get`, `start`, `signal`, `listSignals`, `cancel`, `openStream` (returns `EventSource` for SSE).
- [ ] `WorkItemTable` columns: Title, Status pill, Waiting on (role chip when `currentStatus==WaitingOnCheckpoint`), Updated.
- [ ] "Start work" button is only enabled when the caller's session indicates they hold the start-role (operator falls through). Disabled state shows a tooltip: "Only role X can start work in this project."
- [ ] Status filter is a pill bar (`All`, `Running`, `WaitingOnCheckpoint`, `Completed`, `Failed`, `Cancelled`).
- [ ] "Waiting on me" toggle hits `?waitingOnMe=true`.
- [ ] StartWorkModal: free-form `title` + a JSON textarea for `input` (with `JSON.parse` validation, friendly error). On 409 (no binding) and 502 (executor failure) it surfaces inline `AppErrorBanner` inside the modal.
- [ ] Specs: page (loads, renders rows, status filter changes, waitingOnMe toggle), modal (validation, submit happy path, JSON-parse error, 409 surface).

**Files to Modify/Create:**
- Create: `client/src/app/core/api/work-items.{service,types}.ts`
- Modify: `client/src/app/features/projects/project-home.page.{ts,html,spec.ts}` (mount the table + start button; remove placeholder)
- Create: `client/src/app/features/projects/work-items/work-item-table.{ts,html,spec.ts}`
- Create: `client/src/app/features/projects/work-items/start-work.modal.{ts,html,spec.ts}`
- Create: `mockups/project-home-with-workitems.html`

**Technical Notes:**
"Caller holds the start-role" hint: the JWT memberships claim (FEAT-002) lists `(projectId, roles)` tuples per member; the Project home page already has the project id. v1: just check the caller has *any* role on the project for the button-enabled state; the API still authoritatively enforces the start-role. Hover tooltip references the actual role name from the executor's start contract (fetched via `GET /projects/{id}/work-items` 403 details on first attempt is too late; instead fetch the executor's start contract via the executor router endpoint — or, simpler, render the button enabled and let the server respond with 403). v1 ships the latter; document the affordance gap.

---

### T-040: Work-item detail page (generic, executor-agnostic)

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M · **Dependencies:** T-036, T-037, T-039

**Description:**
Land `/projects/:slug/work-items/:id` per UI Spec §5. Renders title, status badge, executor chip, opaque `executorState` key-value list, `SignalHistoryList`, and `StreamFeed` (SSE pass-through). When the project type maps to `feature-delivery`, render an "Open review →" CTA that routes to `/review` (T-041 lands the review screen).

**Rationale:**
Generic surface — every executor we register gets a working detail page for free.

**Acceptance Criteria:**
- [ ] Mockup at `mockups/work-item-detail.html`.
- [ ] Loads via `GET /api/projects/{id}/work-items/{wid}` + `GET /signals` in parallel; loading state shows skeletons.
- [ ] `StreamFeed` opens an `EventSource` to `/stream` and appends events, `aria-live="polite"`. Disconnect on component destroy. Reconnect button on `error`.
- [ ] `SignalHistoryList` renders the last 20 signals with member + outcome + signaled-at; "Load more" appends.
- [ ] `ExecutorStatePanel` renders `executorState` as a key-value list, with nested objects collapsible.
- [ ] Cancel button is shown only when the caller has the cancel role (or operator); confirmation dialog before submit.
- [ ] 403 → friendly forbidden page; 404 → "Work item not found" with link back to project.

**Files to Modify/Create:**
- Create: `client/src/app/features/projects/work-items/work-item-detail.page.{ts,html,spec.ts}`
- Create: `client/src/app/features/projects/work-items/components/executor-state-panel.{ts,html}`
- Create: `client/src/app/features/projects/work-items/components/signal-history-list.{ts,html}`
- Create: `client/src/app/features/projects/work-items/components/stream-feed.{ts,html}`
- Modify: `client/src/app/app.routes.ts` (add `/projects/:slug/work-items/:id`)
- Create: `mockups/work-item-detail.html`

**Technical Notes:**
`EventSource` doesn't honor `Authorization` headers; v1 ships SSE behind the same JWT cookie path the SPA already uses (refresh cookie is httpOnly). For the access token, we use a workaround: append `?access_token=...` query param at connection time. The backend SSE controller already authorizes from `Authorization` header OR `access_token` query, so document this in the controller comments.

Cancellation idempotency: the cancel confirm dialog passes a fresh `Idempotency-Key` per attempt; if the user clicks twice the server returns the same response.

---

### T-041: Lifecycle review (feature-delivery) — checkpoint action bar + timeline

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** L · **Dependencies:** T-040

**Description:**
The "lifecycle-aware screen" required by the stakeholder definition. `/projects/:slug/work-items/:id/review`. Renders a feature-delivery-shaped timeline (brief → tasks → plan → implementation), an `ActiveStepArtefactPanel` (diff / markdown / fallback), `DecisionHistoryList`, the `CheckpointActionBar` (role-gated outcome buttons + payload editor), and the `StreamFeed`. UI Spec §6.

**Rationale:**
Stakeholder commits to "at least one lifecycle-aware screen." Without it the v1 doesn't ship its required affordance.

**Acceptance Criteria:**
- [ ] Mockups at `mockups/work-item-review.html` covering: default (role holds required role; actions enabled), read-only (lacks role), no-active-checkpoint (banner), submitting, submit-failure.
- [ ] Loads via the same `GET /work-items/{wid}` + `GET /signals` + `GET /checkpoints/{currentKey}` triad.
- [ ] `LifecycleTimeline` builds from `executorState.steps` + `signals` (executor-shaped — opaque; component defines the shape it expects for feature-delivery and ignores unknown extras).
- [ ] `CheckpointActionBar` renders one button per `allowedOutcome` from the contract; only enabled when the caller's role matches `requiredRoleKey` (or operator). Payload editor accepts free-form text (becomes `payload.notes`).
- [ ] On submit: `POST /checkpoints/{key}/signal` with `Idempotency-Key` (generated per attempt). On success, refetch the work item and route to the next active step (or show the completed banner).
- [ ] When the SSE stream signals checkpoint resolution, refetch automatically (debounce 250ms in case multiple events fire).
- [ ] Specs: timeline renders ordered steps, action bar is role-gated, submit happy path, 400 outcome-mismatch surfaced, 502 executor-failure renders the executor key + correlation id with copy button.

**Files to Modify/Create:**
- Create: `client/src/app/features/projects/work-items/review.page.{ts,html,spec.ts}`
- Create: `client/src/app/features/projects/work-items/components/lifecycle-timeline.{ts,html}`
- Create: `client/src/app/features/projects/work-items/components/active-step-artefact-panel.{ts,html}` (with `diff-viewer`, `markdown-renderer`, `artefact-fallback` sub-components — minimal v1 shapes)
- Create: `client/src/app/features/projects/work-items/components/decision-history-list.{ts,html}`
- Create: `client/src/app/features/projects/work-items/components/checkpoint-action-bar.{ts,html}`
- Modify: `client/src/app/app.routes.ts` (add `/projects/:slug/work-items/:id/review`)
- Create: `mockups/work-item-review.html`

**Technical Notes:**
Detect "feature-delivery shape" by checking `workItem.executor.key` against a v1 allowlist (`['feature-delivery-v1']`); show the "Open review" CTA on T-040's detail page only for those. The review page itself doesn't enforce the shape — it renders the timeline best-effort and falls back to a friendly "this lifecycle isn't supported yet" empty state if `executorState.steps` is missing.

DiffViewer / MarkdownRenderer: v1 ships hand-rolled minimal renderers (no external libs). DiffViewer accepts pre-computed `{added, removed, context}` lines; MarkdownRenderer handles headings, lists, code blocks, paragraphs (no images/links in v1). If the artefact shape doesn't match either, `ArtefactFallback` shows the raw JSON.

---

## Summary

| Group | Count | Tasks |
|-------|-------|-------|
| Backend | 4 | T-034, T-035, T-036, T-037 |
| Testing | 1 | T-038 |
| Frontend | 3 | T-039, T-040, T-041 |
| **Total** | **8** | |

**Complexity:** S=1, M=4, L=2, XL=1.

**Critical path:** T-034 → T-035 → T-036 → T-037 → T-038. Frontend (T-039) parallelizes after T-036; T-040 needs T-037 (stream); T-041 needs T-040.

**Risk register:**
- **SSE + JWT** — `EventSource` doesn't accept custom headers. Either (a) accept the access token via `?access_token=` query and document the security implications (short TTL, server-side validation identical to Bearer), or (b) ship a small reusable `fetch`-based SSE polyfill (more code, more correct). v1 picks (a); flag for FEAT-006 review.
- **Idempotency window** — 24h is arbitrary; tune later. v1 wires the unique constraint + a `MaxRetentionDays` env knob.
- **Latency AC** — AC-2 is a *soft* assertion; we run on the same loopback in tests, so ratios are stable but absolute values aren't comparable to a prod-shape executor. Document as "loopback-shape latency only."
- **`startRole` discovery** — the convention "executor ships a `start` checkpoint contract" is documented but not enforced. If an executor author forgets, project starts fall back to `operator`. v1 accepts the fallback; FEAT-003 admin UI could warn at registration time (FEAT-006 polish).
- **Feature-delivery shape coupling** — `LifecycleReview` knows about `executorState.steps`. If the feature-delivery executor evolves its shape we update the page. Document that "lifecycle-aware screens are PER executor key" and future shapes get their own pages.
- **Executor failure visibility** — `Failed` audits with `null executorResponseStatus` are the "stuck signal" signal. FEAT-006's operator dashboard needs to surface these; track that explicitly.

## Post-Generation Checklist

- [x] All FEAT-004 ACs are covered (AC-1↔T-037+T-038, AC-2↔T-038, AC-3↔T-036+T-038, AC-4↔T-037+T-038, AC-5↔T-036+T-038, AC-6↔T-036+T-038).
- [x] Migrations precede services (T-034 → T-036).
- [x] HTTP client + fake executor precede the controllers that consume them (T-035 → T-036).
- [x] Stream endpoint depends on the JSON endpoints (T-036 → T-037) for the auth + audit primitives.
- [x] Each frontend task is mockup-first.
- [x] Dependency graph is acyclic.
- [x] No task violates the Stakeholder scope lock (no cross-project work items, no replay, no SSE-fallback polling).
