# Implementation Plan: T-093 — Confirm BUG-001 root-cause hypothesis

## Task Reference
- **Task ID:** T-093
- **Type:** Investigation
- **Workflow:** investigation-first
- **Complexity:** S
- **Rationale:** PR #77 modified `OrchestratorExecutorClient.cs` for unrelated reasons. Confirm the BUG-001 §5 hypothesis and Option-1 fix primitive (`StepDto.nodeInputs.taskId` in the trace) still hold against `main` before writing code.

## Overview
Read three files (one in DevHub, two in the orchestrator) and the FakeOrchestrator harness; document findings inline in the T-094 commit message and in this plan's "Findings" section below. No code change.

## Investigation Steps

### Step 1: Confirm DevHub still has the buggy fallback
**Files:** `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs`, `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs`
**Action:** Read

- Check `OrchestratorExecutorClient.cs` around lines 80-110. Confirm the `currentTaskId` derivation still funnels through `ExecutorStateProjection.LatestSignalTaskId`.
- Check `ExecutorStateProjection.cs:71`. Confirm `LatestSignalTaskId` still scans only `kind == "signal"` records.
- Record findings inline below.

### Step 2: Confirm orchestrator's `lastStep` lacks `nodeInputs`
**Files:** `../carestechs-agent-orchestrator/src/app/modules/ai/schemas.py`
**Action:** Read

- Locate `LastStepSummary` (per BUG-001 §5: schemas.py:60-66). Confirm it exposes only `id`, `stepNumber`, `nodeName`, `status`.
- Locate the full `StepDto`. Confirm it carries `nodeInputs` as a dict.
- Record findings.

### Step 3: Confirm `nodeInputs.taskId` is the awaited task id at the pause point
**Files:** `../carestechs-agent-orchestrator/src/app/modules/ai/runtime_deterministic.py`, `.../memory.py`
**Action:** Read

- Inspect `runtime_deterministic.py` near line 246 (per BUG-001 §5). Confirm the deterministic runtime injects `nodeInputs.taskId` from `LifecycleMemory.current_task_id` at dispatch time.
- Confirm: between two consecutive pauses on a per-task checkpoint, the latest dispatched step's `nodeInputs.taskId` is the task the orchestrator is *currently awaiting a signal on*, not a previously-resolved task.
- Record findings.

### Step 4: Confirm FakeOrchestrator's `TraceRecord` needs extending
**Files:** `tests/DevHub.TestHarness/FakeOrchestrator/ScriptedRunResponses.cs`, `tests/DevHub.TestHarness/FakeOrchestrator/FakeOrchestratorHost.cs`
**Action:** Read

- Check `TraceRecord`. Confirm it lacks a `NodeInputs` property.
- Check the NDJSON emitter (currently around `FakeOrchestratorHost.cs:154-171`). Confirm only `kind`, `name`, `taskId`, `payload` are serialized.
- Record findings.

## Findings (filled in 2026-05-19)

- **Step 1 (DevHub bug present?):** ✅ Confirmed. `OrchestratorExecutorClient.cs:102-103` falls back to `ExecutorStateProjection.LatestSignalTaskId` (`ExecutorStateProjection.cs:71`). No upstream change has resolved the symptom.
- **Step 2 (lastStep schema confirms no nodeInputs?):** ✅ Confirmed. `LastStepSummary` (`schemas.py:60-66`) has only `id`, `step_number`, `node_name`, `status`. `StepDto` (`schemas.py:85-95`) has `node_inputs: dict[str, Any]`.
- **Step 3 (nodeInputs.taskId is the awaited id?):** ✅ Confirmed. `runtime_deterministic.py:246-248` injects `intake.setdefault("taskId", lifecycle_memory.current_task_id)` then passes `node_inputs=intake` (line 255). The latest dispatched step's `nodeInputs.taskId` IS the awaited task id.
- **Step 4 (FakeOrchestrator extension needed?):** ✅ Confirmed AND extended — see "Unexpected finding" below.
- **Unexpected finding — schema mismatch is wider than the bug report assumed:**
  - The real orchestrator's NDJSON is `{"kind": "...", "data": {...}}` wrapped (`service.py:_serialize_trace_record`, `trace_jsonl.py:53-87`).
  - The real orchestrator's signal `kind` is `"operator_signal"`, not `"signal"` (`_KIND_BY_TYPE` in `service.py:73-78`).
  - Empirical evidence: `carestechs-agent-orchestrator/tests/test_cli_runs.py:246-256` asserts the literal NDJSON bytes; `tests/integration/test_lifecycle_anthropic_mocked.py:239` asserts `"kind":"operator_signal"` is in the trace lines.
  - DevHub's `ParseAssignmentsFromTrace` and `LatestSignalTaskId` both read top-level `name`/`taskId`/`payload` off a `kind == "signal"` record. Against the real orchestrator: every record is discarded at the `kind` check. The assignments projection has been silently broken in production since FEAT-010.
  - Why no end-to-end test caught it: the FakeOrchestrator harness emits the same wrong flat shape DevHub reads, so existing FEAT-010 / T-088 tests pass against a self-consistent fiction.
- **Hypothesis confirmed?:** ✅ Yes — and widened. BUG-001 (and its task breakdown) updated to reflect both the original symptom and the broader schema mismatch.

## Acceptance Verification

- [ ] All four investigation steps performed and findings recorded.
- [ ] Hypothesis explicitly confirmed (or refuted with a follow-on plan).
- [ ] No code change in this task — investigation only.
