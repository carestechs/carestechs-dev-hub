# Feature Brief: FEAT-010 — Orchestrator Adapter (DevHub → carestechs-agent-orchestrator)

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-010 |
| **Name** | Orchestrator Adapter (DevHub → carestechs-agent-orchestrator) |
| **Target Version** | v1 |
| **Status** | Not Started |
| **Priority** | High |
| **Requested By** | Operator (close the route-level gap between DevHub and its intended executor) |
| **Date Created** | 2026-05-17 |

## 2. User Story

**As an** operator running the full DevTools umbrella, **I want** DevHub to actually talk to the carestechs-agent-orchestrator's `/api/v1/runs` API when an operator starts a work item, **so that** the end-to-end flow (start → checkpoint → signal → complete) drives a real lifecycle run instead of returning 404 from a protocol mismatch.

## 3. Goal

Bridge the route-level mismatch between DevHub's executor protocol and the carestechs-agent-orchestrator's actual HTTP surface.

DevHub today calls:
- `POST  {baseUrl}/work-items`
- `GET   {baseUrl}/work-items/{marker}`
- `POST  {baseUrl}/work-items/{marker}/checkpoints/{key}/signal`
- `GET   {baseUrl}/work-items/{marker}/stream`
- `POST  {baseUrl}/work-items/{marker}/cancel`

The orchestrator exposes:
- `POST  {baseUrl}/api/v1/runs` (returns `RunSummaryDto` with `id`)
- `GET   {baseUrl}/api/v1/runs/{run_id}` (returns `RunDetailDto`)
- `POST  {baseUrl}/api/v1/runs/{run_id}/signals` (with `{ name, taskId, payload }`)
- `GET   {baseUrl}/api/v1/runs/{run_id}/trace?follow=true` (NDJSON, not SSE)
- `POST  {baseUrl}/api/v1/runs/{run_id}/cancel`

Plus the orchestrator's `RunStatus` enum (`pending`, `running`, `paused`, `completed`, `failed`, `cancelled`) doesn't match DevHub's `CurrentStatus` strings (`Running`, `WaitingOnCheckpoint`, `Completed`, etc.), and the orchestrator's `RunDetailDto` has no explicit `currentCheckpointKey` — that has to be derived.

This FEAT introduces a **separate adapter service** that listens on DevHub's executor-protocol routes and translates each call to the orchestrator's API, keeping DevHub's `ExecutorHttpClient` unchanged.

## 4. Feature Scope

### 4.1 Included

- **New repository / service: `carestechs-devhub-orchestrator-adapter`** (small Python FastAPI service, sibling to the orchestrator). Sized to live in the umbrella alongside the other services.
- **Five protocol-translating endpoints** (DevHub-protocol-in, orchestrator-protocol-out):
  - `POST /work-items` → `POST /api/v1/runs`.
  - `GET  /work-items/{marker}` → `GET /api/v1/runs/{run_id}` + a status-derivation step (see §6).
  - `POST /work-items/{marker}/checkpoints/{key}/signal` → `POST /api/v1/runs/{run_id}/signals` (mapping `checkpointKey` → `name`, pulling `taskId` + `payload` from the body).
  - `GET  /work-items/{marker}/stream` → `GET /api/v1/runs/{run_id}/trace?follow=true` (NDJSON wrapped into SSE frames).
  - `POST /work-items/{marker}/cancel` → `POST /api/v1/runs/{run_id}/cancel`.
- **Correlation-marker ↔ run-id mapping.** Adapter persists the (marker, run_id, executor_credentials) tuple — in v1 a single SQLite file or even an in-process dictionary backed by a Postgres table on the shared cluster. Marker is the durable key DevHub knows; run_id is the orchestrator-side reference.
- **Auth pass-through.** Adapter accepts DevHub's bearer / API key on inbound calls (whatever DevHub forwards from its `CredentialsRef` resolution), translates to the orchestrator's `require_api_key` model on outbound calls. v1 = static API key per (executor, environment).
- **Status derivation.** Map orchestrator `RunStatus` → DevHub `CurrentStatus`:
  - `pending` / `running` → `Running`.
  - `paused` → `WaitingOnCheckpoint`, with `currentCheckpointKey` derived from the most-recent `awaiting_signal` trace record (the run's most recently emitted "I'm waiting for signal X" event).
  - `completed` → `Completed`. `failed` → `Failed`. `cancelled` → `Cancelled`.
- **`currentTaskId` derivation.** Pull from the run's most recent trace record carrying `current_task_id` (orchestrator IMP-005 surfaces this on per-task pauses). When the run isn't per-task, `null`.
- **`executorState` projection.** Adapter assembles the JSON object DevHub stores opaquely on `WorkItemDto.executorState`. For v1: includes `runId`, `agentRef`, `lastStep` (from `RunDetailDto.lastStep`), and `assignments` (from the orchestrator's `RunMemory.data.assignments` sidecar, fetched once per detail call). The FEAT-009 sidebar reads `executorState.assignments`, so this is the place that bridge lives.
- **`intake.codeSource` pass-through.** The body DevHub sends on `POST /work-items` already includes `intake.codeSource` (FEAT-008). Adapter forwards it verbatim to `POST /api/v1/runs` as part of the `intake` field — no transformation needed at the field level.
- **`intake.workItem` synthesis.** The orchestrator's `CreateRunRequest` expects `intake.workItem` populated from the DevHub WorkItem's title + optional content (sourced from `StartWorkItemRequest.input`, or synthesized from `{ id, title }` when absent). One-page conversion.
- **`agentRef` resolution.** New required field per executor registration. Adapter receives DevHub's call without it; the adapter maps `executor.key` → a configured `agentRef` (e.g. `lifecycle-agent@0.4.0-manual`) via env var / config file. v1 = one agent per adapter instance.
- **NDJSON-to-SSE stream wrapping.** The adapter holds the upstream NDJSON connection open, transforms each line into an SSE `data: <json>\n\n` frame, and forwards to the DevHub client. Pre-flight 404 from the orchestrator → adapter returns 404 to DevHub before opening any body bytes.
- **RFC 7807 error translation.** Orchestrator returns problem-details on errors; adapter preserves the body and status code so DevHub's existing `ExecutorFailureException` path surfaces the right detail.
- **Umbrella deployment.** Adapter joins the existing `devtools-infra` network; container name `devhub-orchestrator-adapter`. Loopback host port for ops curl (e.g. `127.0.0.1:8095`). Added to `../start.sh` PROJECTS list.
- **Smoke test.** A `scripts/verify-adapter.sh` in the new repo that exercises start → fetch → signal → cancel against a freshly-booted umbrella, mirroring the FEAT-007 `verify-umbrella.sh` pattern.
- **Doc updates** in DevHub: `docs/ARCHITECTURE.md` gains an "Executor adapter" section; `docs/api-spec.md` notes that production executor registrations point at the adapter, not the orchestrator directly.

### 4.2 Excluded

- **Rewriting DevHub's `ExecutorHttpClient` to speak the orchestrator's protocol directly.** That's option 3 from the design discussion; deliberately deferred. The adapter keeps DevHub's protocol stable so other executors (or test fakes) can use it too.
- **Multi-agent routing inside one adapter.** v1 ties one adapter instance to one `agentRef`. A second agent = a second adapter container. Cleaner than building dispatch.
- **Real auth bridging (JWT, OAuth, etc.).** v1 = static API key per executor registration.
- **Bidirectional callbacks from orchestrator → DevHub.** The orchestrator's `/hooks/executors/{executor_id}` model is out of scope. DevHub's reconciler still pulls state via fetch; webhooks come later (FEAT-011-ish).
- **Caching / coalescing.** Every DevHub call is a fresh upstream call; if that's too chatty in practice, optimize later.
- **High availability.** v1 = single adapter container per agentRef.
- **GitHub PR linkage on the DevHub WorkItem.** When the orchestrator emits a `github_pr_opened` event, DevHub doesn't learn about it in v1. Plumbed in a separate FEAT.
- **Orchestrator's `/api/v1/work-items` lifecycle endpoints** (S1–S4: open/lock/unlock/close). These are orchestrator-internal and shouldn't surface through the adapter; DevHub doesn't need them.
- **Authoring or modifying agent definitions** (the orchestrator's `GET /api/v1/agents` surface). Out of scope.

## 5. Acceptance Criteria

- **AC-1:** A DevHub operator registers an executor pointing at `http://devhub-orchestrator-adapter:8000` (umbrella DNS) with a configured `agentRef` env var (e.g. `lifecycle-agent@0.4.0-manual`). Adapter health check (`GET /health`) returns 200; orchestrator reachability check returns 200.
- **AC-2 (Start):** From DevHub UI, an operator starts a work item. DevHub posts to `/work-items` on the adapter; adapter posts to the orchestrator's `/api/v1/runs`; orchestrator returns 202 with a `run_id`. Adapter stores the marker↔run-id mapping, returns DevHub's expected `{ currentStatus: "Running", currentCheckpointKey: null, executorState: { runId, agentRef, ... }, currentTaskId: null }`. The DevHub `WorkItem` row gets created with `executorCorrelationMarker = <marker>`.
- **AC-3 (Fetch):** A subsequent `GET /work-items/{marker}` returns the current orchestrator run state, with `currentStatus` mapped from `RunStatus` (e.g. `paused` → `WaitingOnCheckpoint`) and `currentCheckpointKey` derived from the latest pending signal name when paused.
- **AC-4 (Signal):** Posting a signal through DevHub's UI (e.g. `assignment-confirmed` with `{ assignee: "Alice", taskId: "T-001" }`) reaches the orchestrator's `/api/v1/runs/{run_id}/signals` with `{ name: "assignment-confirmed", taskId: "T-001", payload: { assignee: "Alice" } }`. The orchestrator's response (`SignalCreateResponse` with `data` + `meta.alreadyReceived` on dup) is translated to DevHub's `ExecutorSignalResponse` shape.
- **AC-5 (Stream):** Opening the SSE stream from DevHub on `/work-items/{marker}/stream` produces frames; the adapter is reading the orchestrator's NDJSON trace and emitting SSE-formatted events without buffering. Each NDJSON line → one `data: <json>\n\n` frame.
- **AC-6 (Cancel):** Cancelling from DevHub UI hits the orchestrator's `/api/v1/runs/{run_id}/cancel` and the work item transitions to `Cancelled` on the next reconcile.
- **AC-7 (Per-task / FEAT-009):** Driving the lifecycle-agent's manual variant through three tasks works end-to-end: DevHub raises a pending row for T-001 → operator submits `assignment-confirmed` from the `AssignmentConfirmPanel` → orchestrator dispatches T-001 → on `mark_task_done` for T-001 the orchestrator advances `current_task_id` to T-002 → DevHub's reconciler dismisses T-001's row and raises T-002's → and so on. Verified via `scripts/verify-adapter.sh`.
- **AC-8 (Code-source forward / FEAT-008):** Starting a work item against a project with `repo` + `defaultBranch` set, plus an optional `workBranch` override, results in the orchestrator's `RunDetailDto.intake` containing the same `codeSource` block. (No translation — the adapter passes it through.)
- **AC-9 (Errors):** When the orchestrator returns a 4xx/5xx with an RFC 7807 problem-detail body, the adapter passes the body and status through. DevHub's `ExecutorFailureException` path produces a 502 with the orchestrator's correlation id in `details`.
- **AC-10 (Umbrella):** `cd .. && ./start.sh` brings the adapter up alongside DevHub, the orchestrator, the flow engine, and ao-ui. Adapter healthy after `<30s`. `./stop.sh` tears it down cleanly.

## 6. Key Entities and Business Rules

### Marker ↔ run-id mapping

| Field | Source | Lifetime |
|-------|--------|----------|
| `marker` | DevHub-issued UUID (hex, no dashes) on `POST /work-items` | Forever (same row never re-keyed) |
| `run_id` | Orchestrator UUID returned from `POST /api/v1/runs` | Forever (same row never re-keyed) |
| `executor_id` | DevHub's executor registration id (header `X-DevHub-Executor-Id`) | For multi-tenant adapter; v1 single-tenant ignores |
| `agent_ref` | Configured at adapter startup; same for all rows in v1 | Adapter-instance lifetime |

Adapter persists this table in a small Postgres database on the shared cluster (`devhub_orchestrator_adapter`), schema = one table with `(marker PRIMARY KEY, run_id UNIQUE, executor_id, agent_ref, created_at)`.

### Status mapping

| Orchestrator | DevHub | `currentCheckpointKey` |
|---|---|---|
| `pending` | `Running` | `null` |
| `running` | `Running` | `null` |
| `paused` | `WaitingOnCheckpoint` | derived (see below) |
| `completed` | `Completed` | `null` |
| `failed` | `Failed` | `null` |
| `cancelled` | `Cancelled` | `null` |

### `currentCheckpointKey` derivation

The orchestrator's `RunDetailDto` doesn't expose a "what signal am I waiting for" field directly. Three viable sources, in priority order:

1. **Most-recent step's `node_inputs.awaiting_signal`** (if the agent's policy writes that) — preferred when present.
2. **Most-recent trace record of kind `awaiting_signal`** — fallback; requires a one-shot fetch of `/runs/{id}/trace?since=<paused_at>&kind=step`.
3. **Hard-coded fallback to the first expected signal name** from the agent definition — last resort.

Adapter implements #1 and #2; #3 is config-side. Documented in the adapter README.

### `currentTaskId` derivation

Same three-tier strategy:
1. **Most-recent step's `node_inputs.current_task_id`**.
2. **Most-recent trace record carrying `current_task_id`**.
3. `null` when neither source surfaces a value.

### `executorState` projection

Adapter assembles the opaque JSON DevHub stores. v1 contents:
```json
{
  "runId": "<uuid>",
  "agentRef": "lifecycle-agent@0.4.0-manual",
  "lastStep": { "id": "...", "stepNumber": 7, "nodeName": "review_implementation", "status": "completed" },
  "assignments": { "T-001": "Alice", "T-002": "Bob" },
  "stopReason": "done_node | null"
}
```

Assembled by chaining a `GET /runs/{id}` (for the summary + last step) with the run's memory data when a memory endpoint exists, or via a trace scan when it doesn't. v1 implementation can omit `assignments` if the memory isn't directly fetchable — the orchestrator team can extend the API in parallel.

### Auth model

- **Inbound (DevHub → adapter)**: Bearer token in `Authorization` header. Adapter validates against a static `DEVHUB_API_KEY` env var.
- **Outbound (adapter → orchestrator)**: `X-API-Key: <orchestrator-key>` in every call. Configured via `ORCHESTRATOR_API_KEY` env var.
- **No multi-tenant key rotation in v1.** A second DevHub deployment = a second adapter instance.

## 7. API Impact

DevHub side: **none**. The existing `ExecutorHttpClient` continues calling `/work-items`-style routes; the adapter listens on those.

Operationally: every executor registration whose `baseUrl` points at the orchestrator directly must be re-pointed at the adapter's URL (e.g. `http://devhub-orchestrator-adapter:8000`). A short note in `docs/api-spec.md` covers this.

## 8. UI Impact

**None.** The whole point of the adapter is that DevHub doesn't change.

## 9. Edge Cases

- **Marker collision on retry.** DevHub generates a fresh marker per `POST /work-items` call; on retry it re-generates. Adapter dedups by `(marker, X-DevHub-Correlation)` only if DevHub passes a correlation header for retries — otherwise duplicate `POST /api/v1/runs` calls produce duplicate runs on the orchestrator. v1 accepts this; FEAT-011 can add an idempotency key on the adapter.
- **Orchestrator restarts mid-run.** The `run_id` stays valid; the marker↔run-id row keeps working. DevHub's next fetch sees the orchestrator-side state.
- **Adapter restarts.** The marker↔run-id table is persistent (Postgres); no impact on in-flight runs.
- **Orchestrator's NDJSON stream emits a malformed line.** Adapter logs and continues; the SSE consumer never sees the malformed frame. (Bounded log noise; DevHub trace-feed gets a quieter stream than it could.)
- **NDJSON line larger than the SSE max-frame size.** Adapter passes it through anyway; SSE has no enforced max, but proxies may truncate. Documented as a known limitation.
- **DevHub fetches an unknown marker.** Adapter returns `404` matching the orchestrator's not-found shape.
- **Adapter Postgres unreachable.** Health check fails; adapter returns 503 to inbound calls so DevHub's `ExecutorFailureException` triggers correctly (rather than silently 404ing on every marker).
- **Orchestrator returns `paused` but no `awaiting_signal` is derivable.** Adapter returns `currentCheckpointKey: null` and logs a warning. DevHub's reconciler handles null gracefully (no pending rows raised).
- **Operator cancels a run that's already `completed`.** Orchestrator returns the appropriate 409/410; adapter forwards.

## 10. Constraints

- **Single concern.** The adapter is a translation layer, nothing else. No business logic, no caching that introduces staleness, no derived state that the orchestrator doesn't authorize.
- **Trace stream is hot path.** Pass-through SSE means no buffering, no batching, no transformation other than NDJSON→SSE frame conversion. Each line in, one event frame out.
- **Auth is at the boundary.** Adapter validates DevHub's bearer before any upstream call. Denied calls produce a `401` and never reach the orchestrator.
- **Trust the upstream's contract.** Adapter does not re-validate the orchestrator's `intake.codeSource` shape — that's the orchestrator's job. Forwards as-is.
- **DevHub's protocol stays stable.** This adapter does NOT push us toward "DevHub speaks the orchestrator's protocol" — both protocols can coexist; the adapter is the seam.

## 11. Motivation and Priority Justification

**Motivation:** DevHub and the orchestrator were always meant to work together — that's the entire point of DevHub-the-front-door. But the route-level contract drifted during FEAT-003/004 implementation (DevHub defined its own executor wire protocol without grounding against the orchestrator's actual API), and the drift hardened during FEAT-008/009 because field names + checkpoint semantics happened to align even though routes didn't. Without this FEAT, every other FEAT's value is theoretical: the SPA renders correctly, audit captures the right details, notifications fire — but the executor calls 404 out and no real lifecycle ever runs.

**Impact if delayed:** Operators can't actually drive work end-to-end. The whole "single front door" thesis stays unverified in production. Every test we wrote relies on a fake executor; the real one (which exists, in a sibling repo) is unreachable.

**Dependencies:** Builds on everything FEAT-001 through FEAT-009 shipped. Closes the loop. No further FEATs depend on this *strictly*, but every operator-facing scenario does in practice.

## 12. Traceability

| Reference | Link |
|-----------|------|
| **Persona** | Operator |
| **Stakeholder Scope Item** | "DevHub is the only client that holds executor credentials"; "Lifecycle executors are deliberately headless." Both restated; both depend on this FEAT to be true in practice. |
| **Success Metric** | An operator drives a real (orchestrator-backed) work item from start to completion via DevHub UI, without touching the orchestrator's API directly. |
| **Related Work Items** | FEAT-003 (Executor Registry — establishes DevHub's protocol shape). FEAT-008 (Code-Source Binding — relies on the adapter to forward `intake.codeSource`). FEAT-009 (Per-Task Pause — relies on the adapter for signal + status mapping). |
| **Upstream API** | `../carestechs-agent-orchestrator/src/app/modules/ai/router.py` — the `/api/v1/runs` surface this adapter translates against. |
| **Honesty note** | The route-level mismatch was identified in conversation 2026-05-17 after FEAT-009 completed. This FEAT was scoped to close that gap explicitly rather than refactor every existing FEAT's assumptions. |
