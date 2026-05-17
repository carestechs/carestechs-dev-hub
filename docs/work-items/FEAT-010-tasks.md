# FEAT-010 Task Breakdown — Orchestrator Adapter

> Generated from `docs/work-items/FEAT-010-orchestrator-adapter.md` using `.ai-framework/prompts/feature-tasks.md`. 10 tasks across DevOps, Backend (adapter — new repo), and Testing. The bulk of the work lives in a **new sibling repo** (`../carestechs-devhub-orchestrator-adapter`); only a small DevHub-side documentation update lands in this repo.

## Scope choices locked in before generation

- **New sibling repo, not in-process module in DevHub.** Confirmed in the brief: keeps DevHub's `ExecutorHttpClient` and the FakeExecutor protocol stable. The adapter is a standalone Python FastAPI service in `../carestechs-devhub-orchestrator-adapter`.
- **Python FastAPI** matches the orchestrator's language so any future "let's just merge the adapter into the orchestrator" refactor is trivial. Also matches `respx` + `httpx` ergonomics for upstream calls.
- **Single agent per adapter instance in v1.** `AGENT_REF` env var, no dispatch logic. Second agent → second container.
- **`assignments` derivation lives in the adapter via trace scan.** The orchestrator has no `/runs/{id}/memory` endpoint; `RunMemory.data` is only readable via internal service code. The adapter scans `/runs/{id}/trace` for `assignment-confirmed` signal records and rebuilds `{ taskId: assignee }`. v1 acceptable; upstream can add a memory endpoint later and the adapter can switch.
- **Marker↔run-id mapping in shared Postgres.** New DB `devhub_orchestrator_adapter` on the umbrella's shared cluster. One table.
- **Auth: static API keys, both directions.** Inbound bearer = `DEVHUB_API_KEY`; outbound = `ORCHESTRATOR_API_KEY` (`X-API-Key` header). JWT / OAuth not in v1.
- **Sibling-repo tasks ship via that repo's own PRs.** This DevHub task file documents the structure + acceptance; actual commits land in `../carestechs-devhub-orchestrator-adapter`. Same convention as FEAT-007's T-053.
- **One DevHub-side task (T-083)** updates `docs/ARCHITECTURE.md` and `docs/api-spec.md` to point operators at the adapter instead of the orchestrator directly. This is the only PR that lands in this repo.

---

## Foundation

### T-074: Scaffold the adapter repo + umbrella wiring

**Type:** DevOps · **Workflow:** standard · **Complexity:** M · **Dependencies:** None

**Description:**
Bootstrap a new sibling repo `../carestechs-devhub-orchestrator-adapter` with a FastAPI skeleton, `Dockerfile`, `docker-compose.prod.yml` joining the external `devtools-infra` network, `GET /health` endpoint, and an entry in the umbrella's `../start.sh` PROJECTS list. The container is named `devhub-orchestrator-adapter` for cross-project DNS; loopback host port `127.0.0.1:8095` for ops curl.

**Rationale:**
AC-1, AC-10. Every other task assumes the project exists and is reachable.

**Acceptance Criteria:**
- [ ] `../carestechs-devhub-orchestrator-adapter/pyproject.toml` declares `python = "^3.12"`, `fastapi`, `httpx`, `uvicorn`, `sqlalchemy`, `asyncpg`, `pydantic-settings`. Dev deps: `pytest`, `pytest-asyncio`, `respx`.
- [ ] `src/adapter/main.py` exposes a FastAPI app with `GET /health` returning `{ "status": "ok" }` after a one-shot upstream reachability check (`GET {ORCHESTRATOR_BASE_URL}/health`).
- [ ] `Dockerfile` builds a minimal image (~80 MB). Multi-stage; no dev deps in the runtime layer.
- [ ] `docker-compose.prod.yml` declares the service, joins external network `devtools-infra`, publishes `127.0.0.1:8095:8000`, reads env from `.env.production`.
- [ ] `.env.production.example` documents: `DEVHUB_API_KEY`, `ORCHESTRATOR_BASE_URL`, `ORCHESTRATOR_API_KEY`, `AGENT_REF`, `ADAPTER_DB_URL`.
- [ ] `../start.sh` PROJECTS array includes `carestechs-devhub-orchestrator-adapter`.
- [ ] `README.md` documents: purpose, the five translated routes, env vars, smoke-test command.
- [ ] After `cd .. && ./start.sh`, `curl -sf http://127.0.0.1:8095/health` returns 200 within 30 s.

**Files to Modify/Create:**
- Create (sibling repo): `pyproject.toml`, `Dockerfile`, `docker-compose.prod.yml`, `.env.production.example`, `README.md`, `src/adapter/main.py`, `src/adapter/__init__.py`
- Modify (umbrella root): `../start.sh` — add the new project name

**Technical Notes:**
Mirror the orchestrator's structure (`src/app/...`) but use `src/adapter/...` to avoid module-name collision when running in the same Python environment. The umbrella convention from FEAT-007 specifies external network = `devtools-infra` declared `external: true, name: devtools-infra`.

---

### T-075: Marker↔run-id Postgres mapping

**Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-074

**Description:**
SQLAlchemy model + migration for the (marker, run_id, executor_id, agent_ref, created_at) table on the shared cluster's new database `devhub_orchestrator_adapter`. Repository functions: `store`, `find_run_id_by_marker`, `find_marker_by_run_id`.

**Rationale:**
AC-2 + everything downstream. DevHub keys all subsequent calls by `marker`; the adapter has to translate to `run_id`.

**Acceptance Criteria:**
- [ ] `src/adapter/models.py` declares the `MarkerMapping` table with PK on `marker` (text, 64), unique on `run_id` (uuid), index on `executor_id` (uuid).
- [ ] Alembic migration creates the table on first boot. Idempotent (skips if table exists).
- [ ] `src/adapter/store.py` exposes async `store(marker, run_id, executor_id, agent_ref)`, `lookup_run_id(marker) → uuid | None`, `lookup_marker(run_id) → str | None`.
- [ ] Health check returns 503 when the DB is unreachable so DevHub's `ExecutorFailureException` triggers.
- [ ] `../infra/init-databases.sql` (sibling-repo patch) appends `CREATE DATABASE devhub_orchestrator_adapter;` (documented in this task; actual PR opens in `../infra`).
- [ ] Manual one-shot for already-initialized infra volumes documented in the README: `docker exec -i postgres psql -U devtools -d postgres -c 'CREATE DATABASE devhub_orchestrator_adapter;'`.

**Files to Modify/Create:**
- Create (sibling repo): `src/adapter/models.py`, `src/adapter/store.py`, `alembic/env.py`, `alembic/versions/<ts>_initial.py`, `alembic.ini`
- Modify (cross-repo, separate PR): `../infra/init-databases.sql`

**Technical Notes:**
Use `asyncpg` driver — same as the orchestrator. Connection pool size 5 is enough for v1. Don't share a DB context with the orchestrator (their state is opaque to us; ours to them).

---

### T-076: Auth bridge + RFC 7807 error pass-through

**Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-074

**Description:**
Inbound: a small `Depends(require_devhub_auth)` that validates `Authorization: Bearer <DEVHUB_API_KEY>` against the env var. Outbound: a shared `httpx.AsyncClient` instance pre-configured with `X-API-Key: <ORCHESTRATOR_API_KEY>` on every request. Error translation: wrap upstream calls; on non-2xx, parse the RFC 7807 body if present and re-raise with the same status + body.

**Rationale:**
AC-9. Without auth + error translation in place, downstream tasks can't reliably test against either the orchestrator or a mock.

**Acceptance Criteria:**
- [ ] `src/adapter/auth.py` — `require_devhub_auth(request) -> None`. Rejects missing / mismatched bearer with 401 (RFC 7807 body).
- [ ] `src/adapter/upstream.py` exposes a singleton `httpx.AsyncClient` with timeout 60 s (longer for the stream endpoint — handled separately), and a `proxy_call(method, path, **kwargs)` helper that propagates auth + raises a typed `UpstreamError` on non-2xx with `status_code` + parsed RFC 7807 body.
- [ ] `src/adapter/main.py` registers a FastAPI exception handler for `UpstreamError` that returns the upstream's status + body verbatim. DevHub sees the same shape it'd get from a direct orchestrator call.
- [ ] 401 on missing bearer; 401 with mismatched bearer; 502 with `details.upstream_correlation_id` when orchestrator returns 5xx without a problem body.

**Files to Modify/Create:**
- Create (sibling repo): `src/adapter/auth.py`, `src/adapter/upstream.py`, `src/adapter/errors.py`

**Technical Notes:**
The orchestrator wraps every error in `app.core.exceptions` (RFC 7807-shaped). The adapter just forwards. Don't translate field names — `type` / `title` / `detail` / `instance` pass through.

---

## Translation Endpoints

### T-077: `POST /work-items` → `POST /api/v1/runs` (Start)

**Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-075, T-076

**Description:**
DevHub posts `{ input, correlationMarker, intake: { codeSource } }` (FEAT-008 shape). Adapter synthesizes the orchestrator's `CreateRunRequest` shape: `{ agentRef: <env>, intake: { workItem: <synthesized>, codeSource: <forwarded> } }`. On 202, stores the marker↔run-id mapping, returns DevHub's expected `ExecutorStartResponse` shape: `{ currentStatus: "Running", currentCheckpointKey: null, executorState: { runId, agentRef, ... }, currentTaskId: null }`.

**Rationale:**
AC-2, AC-8. The entry point for every work item.

**Acceptance Criteria:**
- [ ] Adapter accepts the DevHub-shaped body. `intake.codeSource` passes through verbatim (the orchestrator's IMP-004 expects this shape).
- [ ] `intake.workItem` synthesized from `input` if `input.workItem` is present, else from `{ id: marker, kind: "DEVHUB", content: <input as JSON string> }` as a fallback.
- [ ] `agentRef` read from `AGENT_REF` env var on startup.
- [ ] On the orchestrator's 202, marker↔run-id row stored before responding to DevHub.
- [ ] Response body matches `ExecutorStartResponse` exactly: `currentStatus`, `currentCheckpointKey`, `executorState` (initial projection — `{ runId, agentRef }` is enough on start), `currentTaskId`.
- [ ] On the orchestrator's 4xx/5xx, the marker↔run-id row is **not** stored, and the error body passes through.
- [ ] At-least-once retry from DevHub (same marker) creates a duplicate run on the orchestrator — explicitly documented as a v1 limitation in the README.

**Files to Modify/Create:**
- Create (sibling repo): `src/adapter/routes/start.py`, `src/adapter/translators/start.py`

**Technical Notes:**
The orchestrator's `RunStatus` after `POST /runs` is typically `pending`, but DevHub's `currentStatus = "Running"` covers both `pending` and `running`. Hardcode `"Running"` on the start response; the subsequent fetch will refine to `WaitingOnCheckpoint` when the run pauses.

---

### T-078: `GET /work-items/{marker}` → `GET /api/v1/runs/{run_id}` (Fetch + status derivation)

**Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** L · **Dependencies:** T-075, T-076

**Description:**
The most complex translation. Lookup `run_id` by marker; fetch the orchestrator's `RunDetailDto`; map `RunStatus` → DevHub `CurrentStatus`; derive `currentCheckpointKey` and `currentTaskId` via a three-tier fallback; assemble `executorState` JSON. Returns `ExecutorFetchResponse`-shaped body.

**Rationale:**
AC-3, AC-7. DevHub's reconciler calls this on every transition; the per-task pending rows depend on `currentCheckpointKey` + `currentTaskId` being accurate.

**Acceptance Criteria:**
- [ ] Marker lookup → 404 (RFC 7807) when not found.
- [ ] `RunStatus` mapping per the brief's table (pending/running → Running, paused → WaitingOnCheckpoint, completed → Completed, failed → Failed, cancelled → Cancelled).
- [ ] `currentCheckpointKey` derivation, in priority order:
  1. `RunDetailDto.lastStep.nodeName` when it indicates an await (e.g. starts with `confirm_` or `await_`) — read agent-side config to know which.
  2. Trace scan: `GET /runs/{run_id}/trace?kind=awaiting_signal` (one-shot, no `follow`) and take the most recent record.
  3. `null` (logged at INFO).
- [ ] `currentTaskId` derivation:
  1. From the same trace record (records carry `current_task_id` per IMP-005 when set).
  2. From the most recent `assignment-confirmed` signal's `task_id` if no current pause record (rare).
  3. `null`.
- [ ] `executorState` projection assembled from `RunDetailDto` + a trace scan for `assignments` records:
  ```json
  {
    "runId": "<uuid>",
    "agentRef": "<agentRef>",
    "lastStep": { ... } | null,
    "assignments": { "T-001": "Alice", "T-002": "Bob" } | {},
    "stopReason": "done_node" | null
  }
  ```
- [ ] When the orchestrator's trace endpoint returns nothing useful for derivation, `currentCheckpointKey: null`, `currentTaskId: null` — never an error.

**Files to Modify/Create:**
- Create (sibling repo): `src/adapter/routes/fetch.py`, `src/adapter/translators/fetch.py`, `src/adapter/translators/state.py`, `src/adapter/translators/trace_scan.py`

**Technical Notes:**
The trace scan reads the last N records of kinds `awaiting_signal` and `signal` (orchestrator's standard trace kinds). For `assignments`, walk the full trace and replay every `assignment-confirmed` signal — bounded by the run's lifetime, typically tens of records. Cache the result for a short TTL (60 s) within the request to avoid double-fetching on a single `GET /work-items/{marker}`.

---

### T-079: `POST /work-items/{marker}/checkpoints/{key}/signal` → `POST /api/v1/runs/{run_id}/signals`

**Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-075, T-076

**Description:**
Translate DevHub's signal call to the orchestrator's. Body: `{ outcome, payload, taskId }` → `{ name: <checkpointKey>, payload: <payload>, taskId: <taskId> }`. The orchestrator's `name` is what DevHub calls `checkpointKey` — straight mapping.

**Rationale:**
AC-4, AC-7. The operator-facing path for every checkpoint signal (including FEAT-009 `assignment-confirmed`).

**Acceptance Criteria:**
- [ ] Marker lookup → 404 when not found.
- [ ] Orchestrator signal request body: `{ name: <checkpointKey>, taskId: <taskId or null>, payload: <payload> }`.
- [ ] The orchestrator's `outcome` field is implicit in `name` (the signal name is the outcome); DevHub's `outcome` field is **ignored** during translation since the orchestrator routes by signal name not outcome value. Document this in the translator's docstring.
- [ ] On the orchestrator's 202 with `meta.alreadyReceived=true` (orchestrator's signal idempotency), the adapter still returns DevHub's `ExecutorSignalResponse` shape — DevHub sees no difference.
- [ ] Response body shape: `{ currentStatus, currentCheckpointKey, executorState, httpStatus, currentTaskId }`. Re-derived via the same path T-078 uses (the orchestrator's signal response doesn't carry the run's new state — adapter has to fetch).

**Files to Modify/Create:**
- Create (sibling repo): `src/adapter/routes/signal.py`, `src/adapter/translators/signal.py`

**Technical Notes:**
The orchestrator's signal endpoint returns 202 + a `SignalCreateResponse` with `data: SignalDto` (the persisted signal row), not the run's new state. The adapter has to follow up with a `GET /runs/{run_id}` to surface the new state — same code path as T-078. Factor `fetch_run_state(run_id)` into a shared helper.

---

### T-080: `POST /work-items/{marker}/cancel` → `POST /api/v1/runs/{run_id}/cancel`

**Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-075, T-076

**Description:**
Simplest translation. Marker → run_id → forward.

**Rationale:**
AC-6. Cancel-path closes the lifecycle from DevHub's UI.

**Acceptance Criteria:**
- [ ] Marker lookup → 404.
- [ ] Orchestrator's `CancelRunRequest` body shape: `{ reason: "DevHub operator cancel" }` (or pull from DevHub's body if present — DevHub doesn't send one today).
- [ ] On 200 from orchestrator, adapter returns 204 to match DevHub's existing cancel response expectation.
- [ ] On 409 (already terminal): pass through.

**Files to Modify/Create:**
- Create (sibling repo): `src/adapter/routes/cancel.py`

**Technical Notes:**
DevHub's `ExecutorHttpClient.CancelAsync` doesn't have an explicit response-body expectation (it's `void`). Match 204.

---

### T-081: `GET /work-items/{marker}/stream` → `GET /api/v1/runs/{run_id}/trace?follow=true` (NDJSON→SSE)

**Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** L · **Dependencies:** T-075, T-076

**Description:**
The hot-path translator. Adapter opens a streaming HTTP connection to the orchestrator's `/trace?follow=true`, reads NDJSON line by line, and emits each line as an SSE `data: <json>\n\n` frame to the DevHub client. Pre-flight 404 from the orchestrator becomes a 404 to DevHub before any body bytes.

**Rationale:**
AC-5. SSE pass-through is what makes the live-trace screen feel native.

**Acceptance Criteria:**
- [ ] Marker lookup → 404 before opening the upstream stream.
- [ ] Upstream content-type `application/x-ndjson`; adapter response content-type `text/event-stream`.
- [ ] One NDJSON line in → one SSE frame out. Empty/whitespace lines suppressed. Malformed JSON lines logged at WARN and suppressed.
- [ ] Client disconnect (DevHub closes the SSE) closes the upstream connection within 1 s.
- [ ] Upstream disconnect closes the SSE cleanly (no partial frame).
- [ ] `Cache-Control: no-cache`, `X-Accel-Buffering: no` headers on the response (orchestrator already sets these; adapter preserves them).
- [ ] An initial `: ready\n\n` heartbeat is emitted within 100 ms of the connection opening (matches the FEAT-005 SSE convention DevHub already expects).

**Files to Modify/Create:**
- Create (sibling repo): `src/adapter/routes/stream.py`, `src/adapter/translators/stream.py`

**Technical Notes:**
Use `httpx.AsyncClient.stream()` with `timeout=httpx.Timeout(60.0, read=None)` — long-poll friendly. The translator is a single async generator. FastAPI's `StreamingResponse` wraps it. Don't buffer; flush after every frame.

The orchestrator's NDJSON includes records of varying `kind` (`step`, `policy_call`, `webhook_event`, `awaiting_signal`, `signal`, `dispatch`). DevHub's SSE feed treats them all as opaque events — no filtering at the adapter layer.

---

## Testing + DevHub Docs

### T-082: Adapter test suite (unit + lightweight integration with `respx`)

**Type:** Testing (adapter) · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-077, T-078, T-079, T-080, T-081

**Description:**
`pytest` + `respx` (httpx-based mock library) to cover every translation path against a recorded orchestrator response set. Unit tests for status mapping, `currentCheckpointKey` derivation, and `executorState` assembly. Integration tests that drive a request through the FastAPI app while mocking upstream.

**Rationale:**
The brief's quality bar. T-074 through T-081 are too easy to break silently in subsequent refactors without coverage.

**Acceptance Criteria:**
- [ ] Status mapping table covered: every `RunStatus` value produces the documented `CurrentStatus`.
- [ ] `currentCheckpointKey` derivation: each of the three tiers is exercised; null fallback covered.
- [ ] `executorState` assembly: assignments map built from a multi-signal trace; empty when no signals; `lastStep` populated from `RunDetailDto.lastStep`.
- [ ] Start endpoint: 202 from upstream produces 200 to DevHub with mapping stored; 4xx from upstream produces matching error to DevHub with no mapping stored.
- [ ] Signal endpoint: 202 with `alreadyReceived` flag handled; subsequent fetch returns refreshed state.
- [ ] Stream endpoint: NDJSON-to-SSE conversion exercised with a fixture stream; line counts match; malformed JSON suppressed.
- [ ] At least 25 tests total.

**Files to Modify/Create:**
- Create (sibling repo): `tests/test_status_mapping.py`, `tests/test_checkpoint_derivation.py`, `tests/test_executor_state.py`, `tests/test_start.py`, `tests/test_signal.py`, `tests/test_stream.py`, `tests/conftest.py`

**Technical Notes:**
`respx` lets you `respx_mock.get("...").mock(return_value=httpx.Response(...))`. FastAPI's `TestClient` for the inbound side. Keep tests fast (< 5 s total) by mocking everything — no real Postgres in this suite (covered by T-083's smoke test).

---

### T-083: `verify-adapter.sh` smoke test + DevHub docs update

**Type:** DevOps + Documentation · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-082

**Description:**
**Two deliverables**:

1. **Adapter repo**: `scripts/verify-adapter.sh` — end-to-end smoke against a live umbrella. Boots the adapter + orchestrator + DevHub, starts a work item via DevHub's API, advances through a signal, asserts the run reaches `completed` on the orchestrator side.
2. **DevHub repo**: Update `docs/ARCHITECTURE.md` with an "Executor adapter" section documenting that production executor registrations point at the adapter, not the orchestrator directly. Add a one-line pointer in `docs/api-spec.md` next to the Executor Registry section. Add a `docs/orchestrator-adapter.md` cross-reference file pointing readers at the sibling repo.

**Rationale:**
AC-1, AC-10. Without the smoke script the adapter is "trust the unit tests"; without the docs update operators won't know to point the executor URL at the adapter.

**Acceptance Criteria:**
- [ ] `scripts/verify-adapter.sh` in the sibling repo runs idempotently against a freshly booted umbrella. Tests: health → register executor (pointed at adapter URL) → start work item → fetch shows `Running` → fetch later shows `WaitingOnCheckpoint` with derived `currentCheckpointKey` → signal → fetch shows `Completed`.
- [ ] DevHub `docs/ARCHITECTURE.md`: new "Executor adapter" subsection under the Executor Registry section. Documents: purpose, where it lives, why it exists (route-level mismatch with the orchestrator).
- [ ] DevHub `docs/api-spec.md`: one-line note in the Executor Registry section: "Production registrations point at the adapter URL; see [`docs/orchestrator-adapter.md`](orchestrator-adapter.md)."
- [ ] DevHub `docs/orchestrator-adapter.md`: deployment / operator notes — env vars, URL conventions, where the source lives.
- [ ] DevHub changelog entries in `docs/ARCHITECTURE.md` and `docs/api-spec.md`.

**Files to Modify/Create:**
- Create (sibling repo): `scripts/verify-adapter.sh`
- Modify (this repo): `docs/ARCHITECTURE.md`, `docs/api-spec.md`
- Create (this repo): `docs/orchestrator-adapter.md`

**Technical Notes:**
Mirror the structure of FEAT-007's `scripts/verify-umbrella.sh` — bash, set -euo pipefail, polled checks, descriptive failure messages. The smoke script is what we'll point at when something breaks in production; treat it as the operator's debugging tool, not just CI.

---

## Summary

| Type | Count |
|------|-------|
| DevOps | 1 (T-074) |
| Backend (adapter) | 6 (T-075, T-076, T-077, T-078, T-079, T-080, T-081) — wait, that's 7 |
| Testing | 1 (T-082) |
| Docs + DevOps | 1 (T-083) |
| **Total** | **10** |

| Complexity | Count |
|------------|-------|
| S | 4 (T-075, T-076, T-079, T-080) |
| M | 4 (T-074, T-077, T-082, T-083) |
| L | 2 (T-078, T-081) |

**Critical path:** T-074 → T-075 → T-077 → T-078 (the fetch/state-derivation work — the hardest task) → T-082 → T-083. T-079, T-080, T-081 can land in parallel after T-076.

**Dependency DAG:**
```
T-074 ──┬──→ T-075 ──┐
        │           ├──→ T-077 ──┐
        └──→ T-076 ──┤           ├──→ T-082 ──→ T-083
                     ├──→ T-078 ─┤
                     ├──→ T-079 ─┤
                     ├──→ T-080 ─┤
                     └──→ T-081 ─┘
```

**Risks / open questions:**

- **`currentCheckpointKey` derivation tier 1** depends on the agent definition naming awaits with a prefix (`confirm_*`, `await_*`). If the lifecycle agent doesn't use that convention, tier 1 is empty and we always fall back to tier 2 (trace scan). T-078's plan should call this out and confirm against the agent definition before implementation.
- **`assignments` trace-scan cost.** Each `GET /work-items/{marker}` re-replays the full trace. For long-running multi-task work items this is O(N) per fetch. A 60s in-process cache (per the technical note) keeps it bounded; if it becomes a bottleneck, the upstream team can add a memory endpoint later.
- **Marker idempotency on retry.** v1 has none — duplicate `POST /work-items` from DevHub creates duplicate runs. Mitigation: DevHub doesn't retry start automatically; T-074's README documents the limitation. If this becomes a problem, FEAT-011 adds an `Idempotency-Key` header.
- **Sibling-repo scaffold work.** T-074 stands up a new Python repo. The user works primarily in C# / Angular and may want to spec the Python style separately. The brief assumed FastAPI; the plan should ask before scaffolding rather than make stylistic decisions silently.
- **Verify the orchestrator's trace `kind=awaiting_signal` records exist.** I'm reading the orchestrator's enum that includes `awaiting_signal` as a webhook event type, but the trace store's record kinds aren't enumerated in the brief's research. T-078 should verify this against `../carestechs-agent-orchestrator/src/app/modules/ai/trace.py` (or wherever the trace records are emitted) before implementing tier 2.
