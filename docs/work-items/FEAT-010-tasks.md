# FEAT-010 Task Breakdown — In-process Orchestrator Client

> Generated from `docs/work-items/FEAT-010-orchestrator-client.md` using `.ai-framework/prompts/feature-tasks.md`. **6 tasks**, all landing inside this repo. T-074..T-083 were the earlier "sibling adapter service" framing — superseded; their numbers are not reused so the global task-ID space stays unique.

## Scope choices locked in before generation

- **In-process implementation.** A new class in `DevHub.Modules.WorkItems`, not a separate service. Reason: same reasoning as the revised brief (§11) — DevHub has one intended executor, the `WorkItem` row already has the right place to store the run id, and a separate Python service was net cost with no benefit.
- **Two `IExecutorHttpClient` implementations side-by-side.** The existing `ExecutorHttpClient` (devhub protocol; speaks `/work-items`) stays for the FakeExecutor and any future devhub-protocol executor. The new `OrchestratorExecutorClient` (orchestrator protocol; speaks `/api/v1/runs`) handles the real production path. DI selects based on `ExecutorRegistrationDescriptor.Protocol`.
- **`Protocol` field defaults to `"devhub"`.** Existing executor registrations get the legacy behavior on migration; production registrations explicitly set `"orchestrator"`. Hard requirement: all 190 existing backend tests pass unchanged.
- **`WorkItem.ExecutorRunId` lives on the existing row.** No new mapping table.
- **NDJSON-to-SSE conversion happens inside `OrchestratorExecutorClient.OpenStreamAsync`.** Keeps protocol translation in one class; `WorkItemStreamForwarder` continues to flush the stream it's given.
- **Trace-scan replays for `assignments`.** The orchestrator has no `/runs/{id}/memory` endpoint; the client replays `assignment-confirmed` records from the trace. v1 fine for typical run lengths.
- **Tier-1 `awaiting_signal` field name unverified** against the lifecycle agent's actual node definitions. T-085's plan should grep the agent definitions before locking the derivation logic.

---

## Foundation

### T-084: EF migrations — `WorkItem.ExecutorRunId` + `ExecutorRegistration.Protocol`

**Type:** Database · **Workflow:** standard · **Complexity:** S · **Dependencies:** None

**Description:**
Two nullable / defaulted columns across two modules. Entity + DbContext config + EF migrations.

**Rationale:**
AC-1, AC-2, AC-9. Every other task in this FEAT reads from these columns.

**Acceptance Criteria:**
- [ ] `WorkItem.ExecutorRunId` added (`Guid?`, nullable). EF migration on `WorkItemsDbContext`.
- [ ] `ExecutorRegistration.Protocol` added (`string`, max 20, default `"devhub"`). EF migration on `ExecutorRegistryDbContext`. Persist via `HasDefaultValue("devhub")` so existing rows backfill.
- [ ] `ExecutorRegistrationDescriptor` (cross-module record) gains `Protocol` with default `"devhub"`.
- [ ] `docs/data-model.md` — field rows for both new columns + changelog row.
- [ ] `dotnet test` 190/190 still green.

**Files to Modify/Create:**
- Modify: `src/DevHub.Modules.WorkItems/Entities/WorkItem.cs`
- Modify: `src/DevHub.Modules.WorkItems/WorkItemsDbContext.cs`
- Modify: `src/DevHub.Modules.ExecutorRegistry/Entities/ExecutorRegistration.cs`
- Modify: `src/DevHub.Modules.ExecutorRegistry/ExecutorRegistryDbContext.cs`
- Modify: `src/DevHub.Contracts/Executors/ExecutorRegistrationDescriptor.cs`
- Modify: `src/DevHub.Modules.ExecutorRegistry/Services/ExecutorRouter.cs` (Map function projects `Protocol`)
- Create: 2 migrations
- Modify: `docs/data-model.md`

**Technical Notes:**
Mirror the FEAT-009 / T-064 pattern (record default values on positional ctors; `HasDefaultValue` on the EF property). `ExecutorRunId` is a value column with no index in v1 — DevHub looks up by WorkItem id, then reads `ExecutorRunId` off the row.

---

## Backend

### T-085: `OrchestratorExecutorClient` — IExecutorHttpClient implementation

**Type:** Backend · **Workflow:** standard · **Complexity:** L · **Dependencies:** T-084

**Description:**
New class `DevHub.Modules.WorkItems.Services.Orchestrator.OrchestratorExecutorClient` implementing `IExecutorHttpClient` against the orchestrator's `/api/v1/runs` API. Includes status mapping, three-tier `currentCheckpointKey` derivation, `currentTaskId` derivation, `assignments` projection from trace replay, and NDJSON-to-SSE conversion inside `OpenStreamAsync`.

**Rationale:**
AC-2..AC-8 + AC-10. The bulk of the FEAT.

**Acceptance Criteria:**
- [ ] New file `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs`. Implements every method on `IExecutorHttpClient` against the orchestrator's `/api/v1/runs[/{id}/...]` routes.
- [ ] `StartAsync` posts `{ agentRef, intake: { workItem, codeSource? } }` to `POST /api/v1/runs`; on success, returns `ExecutorStartResponse` with `runId` on the `ExecutorState` JSON. The actual `WorkItem.ExecutorRunId` persistence happens in T-086 (service layer); the client just surfaces the value.
- [ ] `FetchStateAsync` issues `GET /api/v1/runs/{runId}`, then runs the three-tier `currentCheckpointKey` + `currentTaskId` derivation and assembles `executorState`.
- [ ] `SignalAsync` posts `{ name = checkpointKey, taskId, payload }` to `POST /api/v1/runs/{runId}/signals`, then refetches state for the response body.
- [ ] `OpenStreamAsync` opens `GET /api/v1/runs/{runId}/trace?follow=true` with no read timeout; returns an `ExecutorStreamConnection` whose stream is a TransformStream emitting `data: <json>\n\n` per NDJSON line. Pre-flight 404 propagates as `ExecutorFailureException(404)` before any body bytes.
- [ ] `CancelAsync` posts to `POST /api/v1/runs/{runId}/cancel` with `{ reason: "DevHub operator cancel" }`. Returns silently on 200/204; throws `ExecutorFailureException` on 4xx.
- [ ] Status mapping table (RunStatus → CurrentStatus) lives in a `static class StatusMapper` next to the client.
- [ ] Three-tier checkpoint derivation in a `static class CheckpointDerivation`. Tier 1: `lastStep.nodeInputs.awaiting_signal`. Tier 2: scan `/runs/{id}/trace?kind=awaiting_signal` for the most recent record. Tier 3: null + INFO log.
- [ ] `assignments` derivation: scan `/runs/{id}/trace?kind=signal`, filter for `name == "assignment-confirmed"`, build `{ taskId: assignee }`.
- [ ] `runId` resolved by reading the `WorkItem.ExecutorRunId` column — the client is given a `correlationMarker` by DevHub today, and needs to convert that to `runId`. **Decision:** since the marker IS the key DevHub uses, T-086 changes the service layer to pass the persisted `ExecutorRunId` directly (the client never sees the marker). This keeps the client cleanly run_id-only.
- [ ] Auth: outbound `X-API-Key: <value-of-CredentialsRef-env-var>` resolved via the existing `IExecutorCredentialResolver`. No new credential mechanism.
- [ ] 190 backend tests still green. New tests land in T-088.

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs`
- Create: `src/DevHub.Modules.WorkItems/Services/Orchestrator/StatusMapper.cs`
- Create: `src/DevHub.Modules.WorkItems/Services/Orchestrator/CheckpointDerivation.cs`
- Create: `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs`
- Create: `src/DevHub.Modules.WorkItems/Services/Orchestrator/NdjsonToSseStream.cs`

**Technical Notes:**
Two key shape changes from the existing `ExecutorHttpClient`:

1. **The interface currently takes `correlationMarker` everywhere.** That maps directly to the DevHub-protocol executor's URL path. For the orchestrator, we need the `run_id`. The simplest path: change `IExecutorHttpClient`'s method signatures from `(executor, correlationMarker, ...)` to `(executor, executorRef, ...)` where `executorRef` is a small record `{ Marker, RunId }`. T-086's plan describes the migration in detail. Both implementations adapt their lookup accordingly. Risk: this is a small breaking change on the interface; covered by the existing 190 tests because both implementations are updated in lockstep.

2. **`OpenStreamAsync` returns a wrapped stream.** The wrapping is a tiny `Stream` subclass that reads from the upstream `HttpResponseMessage.Content` line-by-line and emits SSE-framed bytes. Existing `WorkItemStreamForwarder` just `CopyToAsync`s whatever stream it gets — no changes there.

Before locking the tier-1 derivation, grep `../carestechs-agent-orchestrator/agents/` for `awaiting_signal` / `expected_signal` to verify the actual field name. If different, update `CheckpointDerivation.cs` accordingly.

---

### T-086: Wire `ExecutorRunId` + protocol selection through `WorkItemsService`

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-084, T-085

**Description:**
Two things land in this task: (1) persist `WorkItem.ExecutorRunId` on Start and read it on all subsequent operations; (2) register `OrchestratorExecutorClient` in DI and pick the right `IExecutorHttpClient` per executor based on `descriptor.Protocol`.

**Rationale:**
AC-2, AC-9. Without this, the new client is unreachable.

**Acceptance Criteria:**
- [ ] `IExecutorHttpClient` method signatures adjusted to accept the `WorkItem` (or a new `WorkItemRef { Id, Marker, ExecutorRunId? }` value object) instead of bare `correlationMarker`. Both implementations updated to use the right id internally.
- [ ] `ExecutorHttpClient` (devhub protocol) continues to use `Marker` in its URLs — no behavior change.
- [ ] `OrchestratorExecutorClient` uses `ExecutorRunId` in its URLs — required to be non-null for non-Start operations; null is only valid before the orchestrator's `POST /runs` returns.
- [ ] `WorkItemsService.StartAsync`: after `executorClient.StartAsync` returns, parse `runId` from `startResp.ExecutorState["runId"]` and persist to the new `WorkItem` row before commit.
- [ ] Factory / DI: a new `IExecutorClientFactory.Resolve(descriptor)` returns the right impl. Default `descriptor.Protocol == "devhub"` → existing client; `"orchestrator"` → new client. Document the contract in xmldoc.
- [ ] WorkItemsService injects the factory, not the client directly. Every existing call-site updated.
- [ ] `docs/api-spec.md`: ExecutorRegistration request/response gains `protocol`; WorkItemDto + WorkItemSummaryDto gain optional `executorRunId`. Changelog row.

**Files to Modify/Create:**
- Modify: `src/DevHub.Contracts/Executors/IExecutorHttpClient.cs` (interface signature change)
- Modify: `src/DevHub.Modules.WorkItems/Services/ExecutorHttpClient.cs` (adapts to new sig)
- Modify: `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs` (uses ExecutorRunId)
- Create: `src/DevHub.Modules.WorkItems/Services/IExecutorClientFactory.cs` + impl
- Modify: `src/DevHub.Modules.WorkItems/WorkItemsModuleExtensions.cs` (DI registration)
- Modify: `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` (persist RunId on Start; use factory)
- Modify: `src/DevHub.Modules.WorkItems/Services/CheckpointSignalsService.cs` (use factory)
- Modify: `src/DevHub.Modules.WorkItems/Controllers/WorkItemsController.cs` (stream forwarder uses factory)
- Modify: `src/DevHub.Modules.ExecutorRegistry/DTOs/*.cs` (Create/Update requests gain `Protocol`; DTO surfaces it)
- Modify: `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs` (`ExecutorRunId`)
- Modify: `docs/api-spec.md`

**Technical Notes:**
The interface signature change is the most visible part. It does touch `ExecutorHttpClient` (existing devhub-protocol implementation) — the change should be mechanical: replace `string correlationMarker` parameters with `WorkItemRef` and read `.Marker` inside the existing implementation. Same auth, same paths.

If the tests get noisy on positional-record changes for `WorkItemDto.ExecutorRunId`, mirror the FEAT-009 / T-065 approach: add it at the end with a default null.

---

### T-087: Operator UI — protocol picker on executor admin

**Type:** Frontend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-086

**Description:**
The admin executors form gains a `Protocol` dropdown (values: `devhub`, `orchestrator`), defaulting to `orchestrator` for new registrations (since that's the real path forward). Display on the executor list/detail surfaces the value.

**Rationale:**
Without it, operators have to write to the DB to flip an executor to the orchestrator protocol. v1 needs UI for this.

**Acceptance Criteria:**
- [ ] Create/edit form on the executors admin page gains a Protocol dropdown.
- [ ] Default for new entries: `"orchestrator"`. The dropdown displays both values; copy makes the distinction clear ("devhub" = "DevHub native — for tests / FakeExecutor"; "orchestrator" = "carestechs-agent-orchestrator").
- [ ] List + detail surfaces the executor's protocol next to status.
- [ ] WorkItem detail page shows `executorRunId` (in a small monospace label next to the existing `marker`).
- [ ] Specs cover: create with default; create with `devhub`; toggle from devhub → orchestrator via update.

**Files to Modify/Create:**
- Modify: `client/src/app/features/admin/executors/executor-form.modal.ts/.html`
- Modify: `client/src/app/features/admin/executors/executors.page.html` (list display)
- Modify: `client/src/app/features/projects/work-items/work-item-detail.page.html` (small `executorRunId` label)
- Modify: `client/src/app/core/api/executor-registry.types.ts` (`protocol` field)
- Modify: `client/src/app/core/api/work-items.types.ts` (`executorRunId` field)
- Modify: `docs/ui-specification.md` (executors admin section + changelog)

**Technical Notes:**
Cheap. The hardest part is making the dropdown copy clear so an operator doesn't accidentally pick `devhub` for a real registration.

---

## Testing

### T-088: Integration tests with a Kestrel-hosted fake orchestrator

**Type:** Testing · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-085, T-086

**Description:**
New `FakeOrchestratorHost` (sibling to the existing `FakeExecutorHost`) listens on `/api/v1/runs/{...}` routes with scripted responses + recorded calls. New test class `OrchestratorExecutorClientTests` and `OrchestratorExecutorEndToEndTests` exercise the client against this fake.

**Rationale:**
The brief's quality bar. Without coverage, the next refactor breaks something silently.

**Acceptance Criteria:**
- [ ] New `tests/DevHub.TestHarness/FakeOrchestrator/FakeOrchestratorHost.cs` + `ScriptedRunResponses.cs` modeled on the existing FakeExecutor harness. Routes implemented: `POST /api/v1/runs`, `GET /api/v1/runs/{id}`, `POST /api/v1/runs/{id}/signals`, `POST /api/v1/runs/{id}/cancel`, `GET /api/v1/runs/{id}/trace` (NDJSON).
- [ ] `DevHubApiFactory` gains a `UseFakeOrchestrator` flag (mutually exclusive with `UseFakeExecutor`). Sets up the test executor registration with `Protocol = "orchestrator"`.
- [ ] New test class `OrchestratorExecutorClientTests` covers:
  - Start forwards `intake.codeSource` + synthesizes `intake.workItem`; persists `ExecutorRunId`.
  - Fetch maps `RunStatus` → `CurrentStatus` for every value; derives `currentCheckpointKey` from tier 1 + tier 2; `null` on tier 3.
  - Signal posts `{ name, taskId, payload }`; ignores `outcome` field.
  - Cancel returns silently on 200/204.
  - Stream emits `data: <json>\n\n` per NDJSON line; malformed JSON suppressed.
- [ ] New test class `OrchestratorExecutorEndToEndTests` covers:
  - FEAT-008 codeSource: project with repo + branch → orchestrator's `intake.codeSource` matches.
  - FEAT-009 per-task: assignment-confirmed signal forwards `taskId` + `payload.assignee` to the orchestrator.
  - Audit invariants preserved (workitem:start, workitem:signal carry the right details).
- [ ] All 190 existing backend tests still pass — the protocol selector defaults to `"devhub"`, the FakeExecutor's tests don't touch `OrchestratorExecutorClient`.

**Files to Modify/Create:**
- Create: `tests/DevHub.TestHarness/FakeOrchestrator/FakeOrchestratorHost.cs`
- Create: `tests/DevHub.TestHarness/FakeOrchestrator/ScriptedRunResponses.cs`
- Modify: `tests/DevHub.TestHarness/DevHubApiFactory.cs` (add UseFakeOrchestrator)
- Create: `tests/DevHub.Modules.WorkItems.Tests/OrchestratorExecutorClientTests.cs`
- Create: `tests/DevHub.Modules.WorkItems.Tests/OrchestratorExecutorEndToEndTests.cs`

**Technical Notes:**
Reuse the FakeExecutor's recording pattern (`CallRecord`, body capture, query helpers). The trace endpoint needs to emit NDJSON lines — async generator or pre-buffered list, both fine for v1.

For per-task trace scenarios, the scripted-responses model needs to support a mutable `RunMemoryAssignments` map (so a test can post a signal and verify the next fetch's `executorState.assignments` includes it). Easiest: keep a process-local map keyed by `runId` in the host, updated by the signal handler.

---

## Docs

### T-089: ARCHITECTURE.md + api-spec.md + brief Status

**Type:** Documentation · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-085 through T-088

**Description:**
Documentation pass. Add an "Executor protocols" section to ARCHITECTURE.md, a routing-table row to api-spec.md, and mark the FEAT-010 brief as Completed.

**Acceptance Criteria:**
- [ ] `docs/ARCHITECTURE.md` gains a section "Executor protocols (FEAT-010)" — explains the two `IExecutorHttpClient` implementations, when each is used, and how the factory selects.
- [ ] `docs/api-spec.md` Executor Registry section notes the `protocol` field with allowed values; Work Items section notes `executorRunId` on the DTO.
- [ ] `docs/work-items/FEAT-010-orchestrator-client.md` Status flipped to **Completed**.
- [ ] Changelog rows in `docs/ARCHITECTURE.md` + `docs/api-spec.md`.

**Files to Modify/Create:**
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/api-spec.md`
- Modify: `docs/work-items/FEAT-010-orchestrator-client.md`

---

## Summary

| Type | Count |
|------|-------|
| Database | 1 (T-084) |
| Backend | 2 (T-085, T-086) |
| Frontend | 1 (T-087) |
| Testing | 1 (T-088) |
| Documentation | 1 (T-089) |
| **Total** | **6** |

**Complexity:** S=2 (T-084, T-087, T-089 — three S), M=2 (T-086, T-088), L=1 (T-085). (T-089 is S but listed in Documentation.)

**Critical path:** T-084 → T-085 → T-086 → T-088 → T-089. T-087 (UI) can land in parallel with T-088.

**Dependency DAG:**

```
T-084 ──→ T-085 ──→ T-086 ──┬──→ T-088 ──→ T-089
                            └──→ T-087 ─────┘
```

**Risks / open questions:**

- **`IExecutorHttpClient` interface signature change.** Replaces `string correlationMarker` with a small `WorkItemRef` value object across the interface. Five method signatures change. Both implementations + every caller (WorkItemsService, CheckpointSignalsService, WorkItemStreamForwarder, every test fake) update in lockstep. T-086's plan should call out the migration order explicitly so the build doesn't break mid-task.
- **Tier-1 `awaiting_signal` field name** unverified against the lifecycle agent's node definitions. T-085's plan should grep first.
- **Trace-scan cost.** Each fetch replays the full `assignment-confirmed` signal history. v1 acceptable for typical run lengths; if it becomes a hotspot, add a per-request cache in T-085.
- **No retry idempotency.** Same as before — DevHub doesn't auto-retry starts, so the risk is low. Could add an `Idempotency-Key` header to the orchestrator's `POST /runs` if it grows.
- **Backward compatibility is load-bearing.** The protocol default = `"devhub"` is what keeps the existing 190 tests passing. Every task should re-run the full suite before merge.
