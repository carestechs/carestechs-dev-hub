# Bug Report: BUG-001 — Orchestrator-protocol `currentTaskId` never populated

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | BUG-001 |
| **Summary** | `WorkItem.CurrentTaskId` stays `null` for orchestrator-protocol executors, even when the orchestrator is paused on a per-task checkpoint. |
| **Severity** | High |
| **Status** | Reported |
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

- The fix shape is constrained by what the orchestrator exposes. Three options, in increasing cost:
  1. **DevHub-side, trace-scanning.** Fetch the *latest* `StepDto` from the trace (it has full `nodeInputs`) and read `nodeInputs.taskId`. Cost: one extra trace read per fetch (or extend the existing trace scan to also collect this).
  2. **DevHub-side, step-endpoint.** If the orchestrator has a "get step N" endpoint that returns full `StepDto`, fetch just the `lastStep.id`'s detail.
  3. **Orchestrator-side change.** Extend `LastStepSummary` (or `RunDetailDto`) to surface `currentTaskId` directly. Cleanest contract but cross-repo coordination.
- Recommended starting hypothesis: **Option 1**, since it stays inside DevHub and reuses the trace fetch we already make for the assignments projection. The trace is fetched once per `FetchStateAsync` already.

---

## 7. Affected Entities and Components

| Entity / Component | How Affected | Reference |
|--------------------|-------------|-----------|
| `WorkItem.CurrentTaskId` | Always null when executor protocol is `"orchestrator"` and the active checkpoint is per-task | `docs/data-model.md` § WorkItem (FEAT-009 fields) |
| `OrchestratorExecutorClient` | `FetchStateAsync` returns null `currentTaskId` due to incoming-signal-only fallback | `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs:83,102-103` |
| `ExecutorStateProjection` | `LatestSignalTaskId` is wrong primitive for this need | `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs:71` |
| `PendingActionSignal` | Per-task rows collapse to a single null-keyed row per checkpoint | `docs/data-model.md` § PendingActionSignal (FEAT-009) |

---

## 8. Impact Assessment

| Dimension | Assessment |
|-----------|------------|
| **Users affected** | Any reviewer using a project whose executor speaks the orchestrator protocol with per-task contracts. Today: every orchestrator-backed project. |
| **Feature affected** | FEAT-009 (Per-task assignment pause) when crossed with FEAT-010 (Orchestrator client). |
| **Data impact** | No data corruption; missing/incorrect projection only. `WorkItem.CurrentTaskId` is `null` where it should be a task id. |
| **Business impact** | Reviewer UX: per-task assignment screens can't distinguish which task is awaiting a decision; pending-row dedup is wrong. No revenue or compliance impact in v1 (no orchestrator-backed projects in prod yet). |

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

- The fix is straightforward but should be paired with a regression test using the existing `FakeOrchestrator` harness — extend `ScriptedRunResponses` to include full `StepDto` records on the trace, and assert `FetchStateAsync` returns the right `currentTaskId` even when no signals have been delivered yet.
- Until fixed, do not ship reviewer-facing per-task UI on top of orchestrator-protocol executors — the discriminator is missing.
