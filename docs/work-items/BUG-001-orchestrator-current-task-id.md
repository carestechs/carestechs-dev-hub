# Bug Report: BUG-001 — Orchestrator-protocol trace contract mismatch

> **Scope expanded 2026-05-19.** Originally filed as "currentTaskId never populated." T-093 investigation surfaced a wider mismatch — DevHub's trace-parsing helpers (`ParseAssignmentsFromTrace`, `LatestSignalTaskId`) read a flat record shape and the wrong `kind` discriminator, while the real orchestrator emits NDJSON wrapped under `data` with kind names `step` / `operator_signal`. Both projections (currentTaskId AND assignments) are silently broken against the real orchestrator. The FakeOrchestrator harness has the same flat shape, so existing tests pass.

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | BUG-001 |
| **Summary** | DevHub's orchestrator-trace parsing layer (`ExecutorStateProjection`) reads a flat record shape and the wrong `kind` values; the real orchestrator wraps every record under `data` and uses `step` / `operator_signal` as kind names. Visible symptoms: `WorkItem.CurrentTaskId` always `null` and `executorState.assignments` always empty for orchestrator-protocol projects with per-task contracts. |
| **Severity** | High |
| **Status** | Resolved |
| **Reported By** | E2E smoke (out-of-repo script, 2026-05-18) — operator observed every transition log entry as "(no task)" while the orchestrator was looping over two distinct tasks. |
| **Date Reported** | 2026-05-18 |
| **Date First Observed** | 2026-05-18 (first cross-system smoke after FEAT-010 merged) |

### Severity Justification

FEAT-009's per-task pending-action contract (`assignment-confirmed`, `plan-confirmed`, etc.) relies on `WorkItem.CurrentTaskId` to discriminate `PendingActionSignal` rows by `(member, work_item, checkpoint, task_id)`. With `CurrentTaskId` stuck at `null` for every orchestrator-backed work item, **all per-task pending rows for a given checkpoint collapse to the same key** — the FEAT-009 surface effectively regresses to single-task semantics whenever the executor speaks the orchestrator protocol. The flow still completes today only because (a) DevHub doesn't require `taskId` on signal forwards and (b) the orchestrator routes per-task signals via its own in-memory `current_task_id`, not from the payload. This is not a coincidence we should depend on.

---

## 2. Steps to Reproduce

**Preconditions:** Umbrella up; DevHub project bound to an executor with `protocol: "orchestrator"`; orchestrator agent definition has per-task checkpoints (e.g. `confirm_assignment`, `confirm_plan`).

1. Start a work item via `POST /api/projects/{pid}/work-items` — DevHub forwards to orchestrator `POST /api/v1/runs`, persists `ExecutorRunId`.
2. Orchestrator advances and pauses on `confirm_assignment` for task `t-1`.
3. `GET /api/projects/{pid}/work-items/{wid}` — DevHub calls `OrchestratorExecutorClient.FetchStateAsync`.
4. **Observe:** Response carries `currentStatus: "WaitingOnCheckpoint"`, `currentCheckpointKey: "assignment-confirmed"`, `currentTaskId: null`.
5. Send signal (orchestrator advances internally using its own `current_task_id`); pause again on `confirm_assignment` for task `t-2`.
6. Fetch again — `currentTaskId` still `null` (or, after a signal carrying a task id has been replayed, it lags one step behind reality).

**Reproducibility:** Always, for any orchestrator-protocol executor whose agent uses per-task checkpoints.

---

## 3. Expected vs Actual Behavior

### Expected Behavior

When the orchestrator is paused on a per-task checkpoint, `WorkItem.CurrentTaskId` reflects the task the orchestrator is currently waiting on (the same id the orchestrator would inject as `intake.taskId` for the next node). FEAT-009's per-task pending rows are correctly discriminated.

### Actual Behavior

`OrchestratorExecutorClient.FetchStateAsync` derives `currentTaskId` from `ExecutorStateProjection.LatestSignalTaskId(traceRecords)` — i.e. the most recent **incoming** signal's `taskId`. Since DevHub's own signal forwards don't carry a `taskId` (DevHub doesn't know one to send until after a fetch), and the orchestrator's first pause precedes any signal, this fallback is null until at least one task-bearing signal has been processed. Even then, it reports the *last signalled* task, not the *currently awaited* one.

---

## 4. Environment

| Field | Value |
|-------|-------|
| **App Version** | DevHub main @ commit bd79227 (after FEAT-010 merge) |
| **Platform** | Backend bug (any client; surfaced via API) |
| **User Context** | Any project bound to an orchestrator-protocol executor with per-task contracts |
| **Deployment** | Umbrella (`./start.sh`) — reproduced in out-of-repo smoke; not yet seen in prod-like deploy |

---

## 5. Error Evidence

### Network / API Evidence

```http
GET /api/projects/{pid}/work-items/{wid}
→ 200
{
  "data": {
    "currentStatus": "WaitingOnCheckpoint",
    "currentCheckpointKey": "assignment-confirmed",
    "currentTaskId": null,                          ← bug
    "executorRunId": "…",
    …
  }
}
```

Smoke transition log (operator-supplied):

```
[t+02s] Running           (no task)
[t+11s] WaitingOnCheckpoint assignment-confirmed (no task)   ← expected: t-1
[t+18s] Running           (no task)
[t+27s] WaitingOnCheckpoint assignment-confirmed (no task)   ← expected: t-2
```

### Relevant code paths

- `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs:83,102-103` — fallback to `LatestSignalTaskId` when `lastStep` doesn't expose a task id.
- `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs:71` — `LatestSignalTaskId` scans trace for *incoming* signals only.
- Orchestrator `RunDetailDto.lastStep` (`carestechs-agent-orchestrator/src/app/modules/ai/schemas.py:60-66`) is `LastStepSummary` — only `id`, `stepNumber`, `nodeName`, `status`. **No `nodeInputs`.** The task id lives in the agent's `LifecycleMemory.current_task_id` (`memory.py:98`) and is injected into the next step's `nodeInputs.taskId` at dispatch time (`runtime_deterministic.py:246`).

---

## 6. Additional Context

| Field | Value |
|-------|-------|
| **Frequency** | Always |
| **First occurrence** | FEAT-010 merge (2026-05-17). Pre-existing devhub-protocol path is unaffected — FakeExecutor surfaces `currentTaskId` directly. |
| **Workaround exists** | No DevHub-side workaround. The orchestrator's own per-task routing masks the symptom for end-to-end flow correctness, but DevHub's UI and FEAT-009 pending-row semantics remain wrong. |
| **Related bugs** | none |
| **Regression** | No — orchestrator protocol is new (FEAT-010); per-task semantics on this protocol never worked. |

### Observations

#### T-093 investigation findings (2026-05-19)

Verified the original symptom against `main` AND uncovered a deeper schema mismatch in DevHub's trace-parsing layer.

**Finding 1 — original symptom still present.** `OrchestratorExecutorClient.cs:102-103` falls back to `ExecutorStateProjection.LatestSignalTaskId`. `LastStepSummary` (orchestrator `schemas.py:60-66`) carries only `id`, `step_number`, `node_name`, `status` — confirmed; no `node_inputs`. The full `StepDto` (`schemas.py:85-95`) carries `node_inputs: dict[str, Any]`. `runtime_deterministic.py:246-248` injects `nodeInputs.taskId = LifecycleMemory.current_task_id` at dispatch, so the latest dispatched step's `nodeInputs.taskId` IS the awaited task id at a pause point.

**Finding 2 — wider mismatch.** DevHub's `ExecutorStateProjection` was written against a flat record shape that the real orchestrator never emits. Concretely:

| Aspect | DevHub reads (`ExecutorStateProjection.cs`) | Real orchestrator emits (`service.py:_serialize_trace_record`, `trace_jsonl.py:53-87`) |
|---|---|---|
| Wrapper | flat: `{kind, name, taskId, payload}` | wrapped: `{kind, data: {…full DTO fields by_alias…}}` |
| Signal `kind` value | `"signal"` | `"operator_signal"` |
| Step records | not read at all | `kind == "step"`, `data` includes `nodeInputs` |
| Empirical evidence | n/a | `carestechs-agent-orchestrator/tests/test_cli_runs.py:246-256` asserts `{"kind":"step","data":{…"nodeInputs":{}…}}`; `tests/integration/test_lifecycle_anthropic_mocked.py:239` asserts `"kind":"operator_signal"` |

Why hasn't end-to-end broken? The orchestrator routes per-task signals via its own in-memory `LifecycleMemory.current_task_id` — DevHub's payloads don't need to be right for happy-path completion. The defect surfaces only in DevHub's *projections* (`currentTaskId`, `executorState.assignments`). The FakeOrchestrator harness emits the same flat shape DevHub reads, so existing FEAT-010 / T-088 tests pass despite the production-side mismatch.

**Finding 3 — assignments projection is also broken in production.** `ParseAssignmentsFromTrace` (`ExecutorStateProjection.cs:46-64`) filters on `kind == "signal"` and reads `rec.taskId` / `rec.payload.assignee` at the top level. Against the real orchestrator's `{"kind":"operator_signal","data":{"name":"assignment-confirmed","taskId":"…","payload":{"assignee":"…"}}}` shape, every record is discarded at the `kind` check. The "Assignments" sidebar (FEAT-009 T-071/T-072) is always empty for orchestrator-protocol projects.

#### Fix shape

Two halves now, not one:

1. **Realign `ExecutorStateProjection` to the real shape.** Filter on `kind == "operator_signal"` for signal records; read `name` / `taskId` / `payload` off `rec.data.*`. Add `LatestStepTaskId(traceRecords)` reading `rec.data.nodeInputs.taskId` from the most recent `kind == "step"` record (BUG-001 original §6 Option 1 — now correctly shaped). Delete `LatestSignalTaskId` — it remains the wrong primitive even after the wrapping is fixed.
2. **Realign the FakeOrchestrator harness to the real shape.** Update `TraceRecord` and the NDJSON emitter to wrap under `data` and use the real kind names. Update the 5 existing `new TraceRecord(...)` callsites accordingly. The harness is a fidelity tool, not an alternative reality.

The orchestrator side is unchanged (Option 3 of the original "increasing cost" ladder is not used — the schema mismatch is fixable entirely on DevHub's side).

---

## 7. Affected Entities and Components

| Entity / Component | How Affected | Reference |
|--------------------|-------------|-----------|
| `WorkItem.CurrentTaskId` | Always null when executor protocol is `"orchestrator"` and the active checkpoint is per-task | `docs/data-model.md` § WorkItem (FEAT-009 fields) |
| `OrchestratorExecutorClient` | `FetchStateAsync` returns null `currentTaskId` due to wrong-primitive fallback **and** wrong-shape parser | `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs:83,102-103` |
| `ExecutorStateProjection.LatestSignalTaskId` | Wrong primitive (signal vs step) AND wrong shape (flat vs `{kind,data}`) AND wrong kind name (`signal` vs `operator_signal`) | `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs:71` |
| `ExecutorStateProjection.ParseAssignmentsFromTrace` | Wrong shape (flat vs `{kind,data}`) AND wrong kind name. Always returns empty dict against the real orchestrator. **FEAT-009 "Assignments" sidebar is silently empty for orchestrator-protocol projects.** | `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs:46-64` |
| `FakeOrchestrator` test harness | `TraceRecord` shape and NDJSON emitter mirror DevHub's wrong reader, masking the production bug from existing tests | `tests/DevHub.TestHarness/FakeOrchestrator/ScriptedRunResponses.cs:39-43`, `FakeOrchestratorHost.cs:154-171` |
| `PendingActionSignal` | Per-task rows collapse to a single null-keyed row per checkpoint (downstream consequence of the `currentTaskId == null` symptom) | `docs/data-model.md` § PendingActionSignal (FEAT-009) |

---

## 8. Impact Assessment

| Dimension | Assessment |
|-----------|------------|
| **Users affected** | Any reviewer using a project whose executor speaks the orchestrator protocol with per-task contracts. Today: every orchestrator-backed project. |
| **Feature affected** | FEAT-009 (Per-task assignment pause) when crossed with FEAT-010 (Orchestrator client). |
| **Data impact** | No data corruption; missing/incorrect projection only. `WorkItem.CurrentTaskId` is `null` where it should be a task id; `executorState.assignments` is `{}` where it should map taskIds to assignees. |
| **Business impact** | Reviewer UX: per-task assignment screens can't distinguish which task is awaiting a decision; pending-row dedup is wrong; the work-item "Assignments" sidebar is always empty. No revenue or compliance impact in v1 (no orchestrator-backed projects in prod yet). |

---

## 9. Traceability

| Reference | Link |
|-----------|------|
| **Related Feature** | FEAT-009 (per-task semantics), FEAT-010 (orchestrator client) |
| **Violated AC** | FEAT-010 AC-3 ("A `GET` of the work item returns the current orchestrator run's state, with `currentStatus` mapped from `RunStatus` and `currentCheckpointKey` derived from the trace") — `currentTaskId` derivation is implicit in this AC for per-task contracts and is wrong. |
| **Spec Reference** | `docs/api-spec.md` § Work Items — `currentTaskId` field semantics (FEAT-009). |
| **Related Work Items** | FEAT-009, FEAT-010 |

---

## 10. Notes

- The fix has two halves now: realign DevHub's parser to the real `{kind, data}` shape AND realign the FakeOrchestrator harness so the parser has something to read against. Regression tests must cover both projections (`currentTaskId` and `assignments`), and both binding states (per-task and root-level).
- Until fixed, do not ship reviewer-facing per-task UI on top of orchestrator-protocol executors — the discriminator is missing AND the assignments map is empty.
- Lesson worth capturing in the runbook for future executor protocols: a fidelity harness (`FakeOrchestrator`) that mirrors *DevHub's reader* rather than *the protocol's writer* will mask shape-mismatch bugs. The harness should be derivable from (or asserted against) the protocol's own integration tests.

**Regression tests added (T-096):**
- Unit: `tests/DevHub.Modules.WorkItems.Tests/ExecutorStateProjectionTests.cs` — 13 tests covering `LatestStepTaskId` and `ParseAssignmentsFromTrace` against the realigned `{kind, data}` shape. Includes a guard (`ParseAssignmentsFromTrace_ignores_legacy_flat_signal_kind_anti_pattern`) that the pre-fix flat-shape records produce an empty assignments map — readmitting them would re-introduce the silent-mismatch class of bug.
- Integration: `tests/DevHub.Modules.WorkItems.Tests/OrchestratorCurrentTaskIdTests.cs` — runs the BUG-001 §2 scenario through DevHub's public API (paused on `confirm_assignment` for `T-001` then `T-002` with **zero signals delivered**) and asserts `currentTaskId` tracks the latest step's `nodeInputs.taskId` on every fetch. Manual sanity-check (one-time, not committed): reverting the `kind == "step"` filter in `LatestStepTaskId` causes this test to fail with `Expected "T-001", found <null>` — the exact symptom from the original bug report.
- Assignments projection: `OrchestratorExecutorClientTests.Fetch_assembles_assignments_map_from_trace_signals` (already existed) continues to pass under the realigned shape — pre-existing test coverage now actually exercises the production code path.
