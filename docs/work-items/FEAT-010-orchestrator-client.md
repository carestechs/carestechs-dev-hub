# Feature Brief: FEAT-010 — In-process Orchestrator Client

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-010 |
| **Name** | In-process Orchestrator Client (DevHub → carestechs-agent-orchestrator) |
| **Target Version** | v1 |
| **Status** | Completed (2026-05-17) |
| **Priority** | High |
| **Requested By** | Operator (close the route-level gap between DevHub and its intended executor) |
| **Date Created** | 2026-05-17 |
| **Supersedes** | The earlier "Orchestrator Adapter" framing of FEAT-010 (separate Python FastAPI service) — see §11 for the history. |

## 2. User Story

**As an** operator running DevHub against the carestechs-agent-orchestrator, **I want** DevHub's `IExecutorHttpClient` to call the orchestrator's `/api/v1/runs` API directly, **so that** the end-to-end flow (start → checkpoint → signal → complete) drives a real lifecycle run without a separate service in between.

## 3. Goal

Close the route-level mismatch between DevHub's executor protocol and the carestechs-agent-orchestrator's actual HTTP surface, **as a new implementation of `IExecutorHttpClient` inside `DevHub.Modules.WorkItems`** rather than a sibling service.

The existing FakeExecutor-compatible client (`ExecutorHttpClient`) stays in place for tests; the new `OrchestratorExecutorClient` is selected at registration time based on a `Protocol` field on `ExecutorRegistration`. DI resolves the right implementation per request.

### Why in-process rather than a separate service

The earlier framing of this FEAT specced a Python FastAPI sibling repo. That added: a new repo, a new container, a new Postgres database (to store marker↔run-id mapping), a second API key, an extra HTTP hop, and a different language than the rest of the codebase. None of those costs bought anything — DevHub has exactly one intended executor and already has a place to store the orchestrator's run id (the `WorkItem` row), so the entire translation can live in a class in the WorkItems module.

## 4. Feature Scope

### 4.1 Included

- **New column `WorkItem.ExecutorRunId`** (nullable uuid). The orchestrator's `Run.id` is stored alongside the existing `ExecutorCorrelationMarker` on the same row. No new database, no new table.
- **New column `ExecutorRegistration.Protocol`** (string, default `"devhub"`). Tags each registered executor as speaking either the existing DevHub-style protocol (`devhub` — what the FakeExecutor uses today) or the orchestrator's `/api/v1/runs` API (`orchestrator`). Operators set `orchestrator` for production registrations.
- **New `OrchestratorExecutorClient : IExecutorHttpClient`** inside `DevHub.Modules.WorkItems.Services.Orchestrator`. Implements all five methods:
  - `StartAsync` → `POST /api/v1/runs` (forwarding `intake.codeSource` from FEAT-008; synthesizing `intake.workItem` from DevHub's `input` + work item identity). Persists the returned `run_id` to `WorkItem.ExecutorRunId`.
  - `FetchStateAsync` → `GET /api/v1/runs/{run_id}`. Maps `RunStatus` → DevHub `CurrentStatus`; derives `currentCheckpointKey` + `currentTaskId` via a three-tier fallback (see §6); assembles `executorState` projection.
  - `SignalAsync` → `POST /api/v1/runs/{run_id}/signals` with `{ name = checkpointKey, taskId, payload }`. Follows up with a state refresh so DevHub gets the new checkpoint info in the response.
  - `OpenStreamAsync` → `GET /api/v1/runs/{run_id}/trace?follow=true`. Wraps the NDJSON stream as SSE frames inline (no separate forwarder service); returns the wrapped `Stream` for `WorkItemStreamForwarder` to flush.
  - `CancelAsync` → `POST /api/v1/runs/{run_id}/cancel`.
- **`IExecutorHttpClient` factory.** A new `ExecutorClientFactory` (or simply a typed-DI registration with a resolver) picks the implementation based on `ExecutorRegistrationDescriptor.Protocol`. Default = `ExecutorHttpClient` (existing, devhub protocol). When `descriptor.Protocol == "orchestrator"`, return `OrchestratorExecutorClient`.
- **Status mapping** (`RunStatus` → `CurrentStatus`):
  - `pending` / `running` → `"Running"`.
  - `paused` → `"WaitingOnCheckpoint"` (with derived `currentCheckpointKey`).
  - `completed` → `"Completed"`. `failed` → `"Failed"`. `cancelled` → `"Cancelled"`.
- **`currentCheckpointKey` + `currentTaskId` derivation.** Three-tier fallback per §6 — derived inside `OrchestratorExecutorClient`, not in DevHub's service layer.
- **`executorState` projection.** Built inside `OrchestratorExecutorClient.FetchStateAsync` from the orchestrator's `RunDetailDto` plus a one-shot trace scan for `assignments`. Surfaces to DevHub as opaque JSON.
- **`assignments` derivation.** The orchestrator has no `/runs/{id}/memory` endpoint; the client replays `assignment-confirmed` signal records from `/api/v1/runs/{id}/trace` (no `follow`) and rebuilds `{ taskId → assignee }` on each `FetchStateAsync` call.
- **`intake.codeSource` pass-through.** Already shipped on the DevHub side via FEAT-008. The client forwards the existing block as-is to the orchestrator's `CreateRunRequest.intake.codeSource`.
- **Auth.** `OrchestratorExecutorClient` reads its outbound API key from the executor registration's existing `CredentialsRef` (env var name). One env var per registered orchestrator. No new auth scaffolding.
- **NDJSON → SSE conversion** inside `OpenStreamAsync`. The orchestrator emits `application/x-ndjson`; DevHub's stream forwarder serves `text/event-stream`. The client wraps the upstream stream in a transform that converts each NDJSON line to `data: <json>\n\n`, then returns the wrapped stream to the existing `WorkItemStreamForwarder`.
- **EF migrations** for both new columns.
- **Test harness reuses the existing pattern.** A new `FakeOrchestratorHost` (sibling to `FakeExecutorHost`) listens on `/api/v1/runs/...` routes and is the testing target for `OrchestratorExecutorClient`. Pre-existing tests using `FakeExecutorHost` continue to work unchanged.
- **Doc updates** in DevHub: `docs/ARCHITECTURE.md` gets an "Executor protocols" section explaining the two implementations; `docs/api-spec.md` notes the `Protocol` field on executor registration; `docs/data-model.md` field rows for the two new columns + changelog.

### 4.2 Excluded

- **Sibling repo / separate adapter service.** Explicitly discarded — see §11 for the reasoning.
- **Replacing `ExecutorHttpClient` (the existing devhub-protocol implementation).** Stays in place for the FakeExecutor + any future executor that speaks DevHub's protocol.
- **Multi-agent dispatch in one client.** Each registered executor has one `CredentialsRef` and one base URL; v1 has no concept of "this client serves multiple agents". A second orchestrator deployment = a second `ExecutorRegistration` row.
- **Webhook callbacks** (orchestrator → DevHub). Out of scope; reconciler still pulls state. Future FEAT.
- **GitHub PR linkage** on the DevHub WorkItem from the orchestrator's `github_pr_opened` events. Out of scope.
- **Idempotency on retry.** v1 lets DevHub's retry semantics through unchanged (no client-side dedupe). The orchestrator already idempotency-keys signals on `(run_id, name, task_id)`; start calls aren't deduped. Acceptable — DevHub doesn't auto-retry starts.
- **Authoring or modifying agent definitions.** No new endpoints around `/api/v1/agents`.
- **Caching / coalescing.** Each call hits the orchestrator. If the trace-scan-on-every-fetch becomes a hotspot, add a per-request cache; not v1's problem.

## 5. Acceptance Criteria

- **AC-1 (Registration):** An operator registers an executor with `protocol = "orchestrator"`, base URL pointing at the orchestrator (e.g. `http://orchestrator-api:8000` on the umbrella), and a `CredentialsRef` resolving to the orchestrator's static API key.
- **AC-2 (Start):** Starting a work item against an `orchestrator`-protocol executor results in `POST {baseUrl}/api/v1/runs` with the synthesized body, the returned `run_id` persisted to `WorkItem.ExecutorRunId`, and DevHub's response carrying the expected `ExecutorStartResponse` shape.
- **AC-3 (Fetch):** A `GET` of the work item returns the current orchestrator run's state, with `currentStatus` mapped from `RunStatus` and `currentCheckpointKey` derived from the trace.
- **AC-4 (Signal):** Submitting a signal (e.g. `assignment-confirmed` with `{ assignee: "Alice", taskId: "T-001" }`) reaches `POST /api/v1/runs/{run_id}/signals` with `{ name: "assignment-confirmed", taskId: "T-001", payload: { assignee: "Alice" } }`. The response includes refreshed state.
- **AC-5 (Stream):** Opening the SSE stream on `/work-items/{wid}/stream` reads the orchestrator's NDJSON trace, converts each line to an SSE frame, and forwards without buffering. Pre-flight 404 from the orchestrator propagates as 404 before any body bytes.
- **AC-6 (Cancel):** Cancelling a work item hits `POST /api/v1/runs/{run_id}/cancel` and DevHub's WorkItem transitions to `Cancelled` on the next reconcile.
- **AC-7 (Per-task / FEAT-009 end-to-end):** Driving the lifecycle-agent manual variant through three tasks works: pending-action rows raised per task, `AssignmentConfirmPanel` submits succeed, loop-back closes T-N and opens T-(N+1).
- **AC-8 (Code-source forward / FEAT-008):** Work item started against a project with `repo` + `defaultBranch` results in the orchestrator's `RunDetailDto.intake.codeSource` containing the same block.
- **AC-9 (Backwards compatibility):** Existing `ExecutorRegistration` rows (no `Protocol` field set, or `Protocol = "devhub"`) continue using the existing `ExecutorHttpClient` against the FakeExecutor and any other devhub-protocol executor. All 190 backend tests continue to pass unchanged.
- **AC-10 (Error translation):** A 4xx/5xx from the orchestrator surfaces through DevHub's existing `ExecutorFailureException` path with the orchestrator's correlation id in `details`.

## 6. Key Entities and Business Rules

### New columns

| Entity | Field | Type | Description |
|---|---|---|---|
| `WorkItem` | `ExecutorRunId` | UUID, nullable | The orchestrator's `Run.id` for this work item. Null for devhub-protocol executors (existing flows). |
| `ExecutorRegistration` | `Protocol` | varchar(20), default `"devhub"` | Selects the `IExecutorHttpClient` implementation. Values: `"devhub"` (existing protocol; FakeExecutor compatible) or `"orchestrator"` (new; `/api/v1/runs`). |

### Status mapping

| Orchestrator `RunStatus` | DevHub `CurrentStatus` | `currentCheckpointKey` |
|---|---|---|
| `pending` | `Running` | `null` |
| `running` | `Running` | `null` |
| `paused` | `WaitingOnCheckpoint` | derived (see below) |
| `completed` | `Completed` | `null` |
| `failed` | `Failed` | `null` |
| `cancelled` | `Cancelled` | `null` |

### `currentCheckpointKey` derivation

Three-tier fallback, in priority order:

1. **`RunDetailDto.lastStep.nodeInputs.awaiting_signal`** when present.
2. **Most-recent trace record of kind `awaiting_signal`** (one-shot GET on `/runs/{id}/trace`, no `follow`).
3. **`null`** (logged at INFO).

### `currentTaskId` derivation

Same three-tier strategy, reading `current_task_id` from the same sources.

### `executorState` projection

Assembled per `FetchStateAsync`:
```json
{
  "runId": "<uuid>",
  "agentRef": "lifecycle-agent@0.4.0-manual",
  "lastStep": { "id": "...", "stepNumber": 7, "nodeName": "review_implementation", "status": "completed" },
  "assignments": { "T-001": "Alice", "T-002": "Bob" },
  "stopReason": "done_node | null"
}
```

`assignments` built by replaying every `assignment-confirmed` signal record from the trace (no `follow`). Bounded by the run's lifetime; in practice tens of records.

### Auth model

- **Inbound (operator → DevHub)**: unchanged — JWT bearer via DevHub's existing auth (FEAT-002).
- **Outbound (DevHub → orchestrator)**: `X-API-Key: <value-of-credentialsRef-env-var>` on every call. The existing `IExecutorCredentialResolver` resolves `executor.CredentialsRef` to an env-var value already; the new client uses the same path.

## 7. API Impact

- `ExecutorRegistration` requests + DTOs gain `protocol` (optional; defaults to `"devhub"`).
- `WorkItemDto` + `WorkItemSummaryDto` could optionally surface `executorRunId` for ops visibility — recommended for v1 (helps operators correlate DevHub and orchestrator logs).
- No new endpoints in DevHub. No changes to `IExecutorHttpClient` interface shape.

## 8. UI Impact

- **Executor registration UI** (admin) gains a protocol picker — dropdown with values `"devhub"` and `"orchestrator"`. Default selection: `"orchestrator"` for new registrations (since that's now the production path), but the form still allows `"devhub"` for test/legacy executors.
- **Work item detail page** (operator) may show `executorRunId` next to `executorCorrelationMarker` for cross-system debugging. Cheap to add; bumps trust.

Other screens (review page, assignments sidebar, pending-actions list) **do not change** — they already read DevHub's abstractions, which the new client populates correctly.

## 9. Edge Cases

- **Existing executor registrations after migration.** Get `protocol = "devhub"` via the default; no behavior change.
- **Orchestrator returns `paused` but no derivable `awaiting_signal`.** Client returns `currentCheckpointKey: null` and logs a WARN. DevHub's reconciler handles null gracefully (no pending rows raised).
- **Trace scan cost on long-running runs.** Each fetch replays the full trace for `assignments`. Bounded by the run's lifetime — typically tens of records. If it becomes a hotspot, add a per-request cache (out of v1 scope).
- **Marker collision on retry.** DevHub doesn't auto-retry starts. If an operator re-clicks rapidly, the duplicate POST creates a duplicate orchestrator run. Same risk as before; not v1's job.
- **NDJSON line size.** No enforced limit on either side; large policy-call payloads pass through SSE intact. Documented limitation if downstream proxies truncate.
- **Orchestrator unreachable.** Existing `ExecutorFailureException` path surfaces 502 with the executor identifier; identical behavior to today's devhub-protocol path.
- **`tier-1 awaiting_signal field name unverified` against the agent definition.** Worth checking before T-085 finalizes the derivation logic — same risk as the previous brief flagged.

## 10. Constraints

- **No new repo, no new service, no new database.** The whole point of the revision.
- **Backwards compatibility is a hard requirement.** Existing 190 backend tests must continue to pass without modification. The selector defaults to the devhub protocol so all existing fixtures keep working.
- **Single concern.** The new client only translates between DevHub's `IExecutorHttpClient` interface and the orchestrator's HTTP surface. No caching, no retries, no business logic.
- **The auth + credential resolution path is shared.** The new client uses the existing `IExecutorCredentialResolver` — no new key management.
- **Streaming is hot path.** NDJSON→SSE conversion is line-by-line, no buffering beyond the line buffer; flush every frame.
- **Trust the orchestrator's contract.** Pass `intake.codeSource` through as-is; don't re-validate (DevHub already validated at the boundary in FEAT-008).

## 11. Motivation and Priority Justification

**Motivation:** DevHub and the orchestrator were always meant to work together, but during FEAT-003/004 implementation I defined DevHub's executor wire protocol generically (`/work-items`, `/signal`, `/stream`) without grounding it against the orchestrator's actual API (`/api/v1/runs`). The drift hardened during FEAT-008/009 — field names + checkpoint semantics happened to align so the body-level changes were correct, but the routes weren't. Without this FEAT, every other FEAT's machinery is theoretical.

### Why this brief supersedes the earlier framing

The first version of this FEAT (`FEAT-010-orchestrator-adapter.md`, merged earlier in the day) specced a separate Python FastAPI service. The user pushed back: *"why FastAPI? Why Python? Why not make the adapter a simple class/classes in the current project?"* That was correct. The reasons I'd given for a separate service didn't hold up under scrutiny:

- DevHub has exactly one intended executor — no "swap executors" benefit.
- A new Postgres database to map marker → run_id was duplicating data that already lives on the `WorkItem` row.
- An extra HTTP hop, an extra container, two API keys, a different language — all cost, no benefit.

This revision deletes the separate-service design entirely. The orchestrator client is a class in `DevHub.Modules.WorkItems` selected via a `Protocol` field on `ExecutorRegistration`. Smaller scope (6 tasks vs 10), one repo, one CI pipeline, no umbrella sprawl.

**Impact if delayed:** Same as the previous framing — DevHub's whole "single front door" thesis stays unverified until the wire contract works against the real orchestrator.

**Dependencies:** Builds on FEAT-001..009. No further FEATs strictly depend on this, but every operator-facing scenario does in practice.

## 12. Traceability

| Reference | Link |
|-----------|------|
| **Persona** | Operator |
| **Stakeholder Scope Item** | "DevHub is the only client that holds executor credentials"; "Lifecycle executors are deliberately headless." Both restated. |
| **Success Metric** | An operator drives a real (orchestrator-backed) work item from start to completion via DevHub UI, without DevHub running anything other than itself + the orchestrator. |
| **Related Work Items** | FEAT-003 (Executor Registry — adds the `Protocol` field), FEAT-008 (Code-Source Binding — relies on `intake.codeSource` forwarding through the new client), FEAT-009 (Per-Task Pause — relies on the new client's signal + status mapping). |
| **Upstream API** | `../carestechs-agent-orchestrator/src/app/modules/ai/router.py` — the `/api/v1/runs` surface this client translates against. |
| **Honesty note** | The original framing of this FEAT (sibling Python adapter service) was wrong. Recorded in §11 so the reasoning trail is visible. |
