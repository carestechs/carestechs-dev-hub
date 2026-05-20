# BUG-001 Task Breakdown — Orchestrator-protocol trace contract mismatch

> Generated from `docs/work-items/BUG-001-orchestrator-current-task-id.md` using `.ai-framework/prompts/bugfix-tasks.md`.
>
> **Scope expanded 2026-05-19** after T-093 investigation surfaced a wider mismatch than the original report. The flat-vs-wrapped NDJSON shape and the wrong `kind` discriminator affect both `currentTaskId` and `assignments` projections; the FakeOrchestrator harness is wrong in the same way as DevHub, masking the bug from existing tests. The amended breakdown realigns DevHub's parser AND the harness, with regression tests for both projections.

## Scope choices locked in before generation

- **Fix everything reachable from the trace, once.** Both `LatestStepTaskId` (new, replaces `LatestSignalTaskId`) and `ParseAssignmentsFromTrace` (rewritten) read the realigned `{kind, data}` shape with `kind ∈ {step, operator_signal}`. We don't ship a half-realigned projection.
- **FakeOrchestrator is the protocol's writer, not DevHub's mirror.** `TraceRecord` becomes a discriminated representation: `Kind` plus a `Data` payload that mirrors the orchestrator's `model_dump(by_alias=True)` shape. The NDJSON emitter wraps as `{"kind": "...", "data": {...}}`. Existing test call sites are updated (5 in total per `grep`).
- **No orchestrator-side change.** The orchestrator's contract is already correct; DevHub is the side that drifted.
- **No production-data migration.** The bug is in derived projections, not stored data. After the fix, the next `FetchStateAsync` on any orchestrator-protocol work item produces correct `currentTaskId` and `assignments`.
- **Investigation is already complete (T-093).** Findings are recorded in BUG-001 §6 and the T-093 plan. The implementation tasks below proceed directly.
- **Out of scope:** Refactoring `ExecutorStateProjection` more broadly; adding a per-step trace cache; introducing a new endpoint to query `executorState.assignments` directly; FEAT-009 schema changes.

---

## Investigation

### T-093: Confirm root cause + surface schema mismatch *(DONE)*

**Type:** Investigation · **Workflow:** investigation-first · **Complexity:** S · **Dependencies:** None

**Status:** Findings recorded in BUG-001 §6 ("T-093 investigation findings"). Plan file `plans/plan-T-093-bug-001-investigation.md`. No production code change.

---

## Implementation

### T-094: Realign `ExecutorStateProjection` to the real orchestrator trace shape

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-093

**Description:**
Rewrite `ExecutorStateProjection` to consume the orchestrator's real NDJSON shape (`{kind, data}` with `kind ∈ {step, operator_signal, …}`). Replace `LatestSignalTaskId` with `LatestStepTaskId` that reads `rec.data.nodeInputs.taskId` from the most recent `step`-kind record. Rewrite `ParseAssignmentsFromTrace` to filter on `kind == "operator_signal"`, `data.name == "assignment-confirmed"`, and read `data.taskId` + `data.payload.assignee`. Wire `LatestStepTaskId` into `OrchestratorExecutorClient.FetchStateAsync` in place of the old fallback.

**Rationale:**
BUG-001 §6 Findings 1, 2, and 3. The current projection helpers are silently broken in production for both `currentTaskId` and `assignments`. Realigning to the contract empirically established by `carestechs-agent-orchestrator/tests/test_cli_runs.py:246-256` and `tests/integration/test_lifecycle_anthropic_mocked.py:239` fixes both projections with one schema-aligned helper set.

**Root Cause Addressed:**
DevHub's parser reads flat `{kind, name, taskId, payload}` records with `kind == "signal"`; the real orchestrator emits `{kind, data: {…}}` records with `kind == "operator_signal"` (for signals) and `kind == "step"` (for dispatched steps, the only place `nodeInputs.taskId` is exposed).

**Implementation Approach:**
1. Add `LatestStepTaskId(IEnumerable<JsonElement>)` — iterates records, picks the last with `kind == "step"`, returns `rec.data.nodeInputs.taskId` (string, non-empty) or `null`.
2. Rewrite `ParseAssignmentsFromTrace` — replace `kind == "signal"` with `"operator_signal"`; read `name` / `taskId` / `payload` off `rec.data.*`.
3. Delete `LatestSignalTaskId` (dead code after the swap; semantically the wrong primitive even after wrapping is fixed).
4. Swap the fallback call in `OrchestratorExecutorClient.FetchStateAsync:102-103`.

**Files to Modify:**
- `src/DevHub.Modules.WorkItems/Services/Orchestrator/ExecutorStateProjection.cs` — rewrite both helpers; delete `LatestSignalTaskId`.
- `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs:98-103` — swap fallback + update comment.

**Acceptance Criteria:**
- [ ] `LatestSignalTaskId` no longer exists. `grep -rn "LatestSignalTaskId" src/ tests/` returns no hits.
- [ ] `LatestStepTaskId` reads `rec.data.nodeInputs.taskId` from the most recent `step`-kind record; returns `null` when missing/empty or no step has been dispatched.
- [ ] `ParseAssignmentsFromTrace` reads `rec.data.*` fields, filters on `kind == "operator_signal"`. Returns a `taskId → assignee` map with the same last-write-wins semantics as before.
- [ ] `FetchStateAsync` derives `currentTaskId` from `LatestStepTaskId`.
- [ ] xmldoc on both helpers cites the orchestrator-side authoritative tests (`test_cli_runs.py:246-256`, `test_lifecycle_anthropic_mocked.py:239`) so future readers can find the contract.

**Regression Risk:**
- **Tests using the old shape fixtures will fail after this task lands.** That is expected and fixed in T-095, which is mandatory and not optional. Do not merge T-094 alone — they ship together.
- **The `lastStep is not null` path in `FetchStateAsync` still reads `lastStep.nodeName` directly.** That code path is correct — `lastStep` (from `RunDetailDto`) is *not* under a `data` wrapper. Only the trace records are wrapped.

---

### T-095: Realign FakeOrchestrator + existing test fixtures to the real shape

**Type:** Testing · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-094 (lands in the same PR)

**Description:**
Update the FakeOrchestrator harness so its NDJSON emitter mirrors the real orchestrator's `{kind, data}` shape with the real `kind` names. Rewrite `TraceRecord` to be a discriminated `{Kind, Data}` representation. Update the 5 existing `new TraceRecord(...)` call sites accordingly. After this task, the harness is a fidelity tool — wrong shapes in tests fail by being unparseable, not by being silently consistent with DevHub's wrong reader.

**Rationale:**
BUG-001 §10 "Lesson worth capturing" — a harness that mirrors DevHub's reader instead of the protocol's writer masks shape-mismatch bugs. This task closes that gap.

**Implementation Approach:**
1. Rewrite `TraceRecord` (`tests/DevHub.TestHarness/FakeOrchestrator/ScriptedRunResponses.cs:39-43`) to `{Kind, Data}` with helpers for the two common kinds: `TraceRecord.Step(taskId, nodeName, …)` and `TraceRecord.OperatorSignal(name, taskId, payload)`. Keep the type small and obvious.
2. Update the emitter at `FakeOrchestratorHost.cs:154-171` to serialize `{kind, data}` (`data` is `rec.Data`).
3. Update auto-append in `FakeOrchestratorHost.cs:121` (where DevHub-side signal forwards are auto-recorded into `TraceRecords`) to construct an `OperatorSignal` record.
4. Update 3 existing call sites in `OrchestratorExecutorClientTests.cs:177-179` to use the new helpers.

**Files to Modify:**
- `tests/DevHub.TestHarness/FakeOrchestrator/ScriptedRunResponses.cs`
- `tests/DevHub.TestHarness/FakeOrchestrator/FakeOrchestratorHost.cs`
- `tests/DevHub.Modules.WorkItems.Tests/OrchestratorExecutorClientTests.cs` (3 lines)

**Acceptance Criteria:**
- [ ] `TraceRecord` shape forces callers to specify a `Kind` and a `Data` payload.
- [ ] NDJSON emitter output for a step record matches the contract in `test_cli_runs.py:246-256` byte-equivalent enough that a copy-pasted real-orchestrator line parses identically.
- [ ] 3 existing call sites updated. `dotnet build` green.
- [ ] No production-code change in this task — implementation only changes test harness + test fixtures.

---

## Verification & Prevention

### T-096: Regression tests for both projections + status flip

**Type:** Testing · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-094 + T-095 (lands in the same PR)

**Description:**
Add unit tests covering both `LatestStepTaskId` and the rewritten `ParseAssignmentsFromTrace`. Add the integration test from BUG-001 §2 (paused on `confirm_assignment` for `t-1` → `t-2` with no signal delivered) and a second integration test asserting `executorState.assignments` reflects an `assignment-confirmed` operator_signal in the trace. Flip BUG-001 to `Resolved` and append the regression-test pointer.

**Rationale:**
BUG-001 §9 "Violated AC" + §10 "regression test mandatory." Both projections are now correct; both need test coverage so the same shape mismatch can't return silently.

**Test Cases:**

*Unit (`ExecutorStateProjectionTests.cs`, new file):*
- `LatestStepTaskId`: empty trace → `null`; trace with only `operator_signal` records → `null`; multiple `step` records with different task ids → returns latest one; latest `step` lacks `nodeInputs` or `nodeInputs.taskId` → `null` (conservative).
- `ParseAssignmentsFromTrace`: filters out non-`operator_signal` kinds; filters out signals where `name != "assignment-confirmed"`; reads `data.taskId` + `data.payload.assignee`; later signals overwrite earlier ones for the same task; skips signals with missing taskId / non-string assignee / empty assignee.

*Integration (`OrchestratorCurrentTaskIdTests.cs`, new file):*
- `Get_work_item_currentTaskId_reflects_latest_step_nodeInputs_taskId` — append one `step` record with `nodeInputs.taskId = "t-1"` (no signals), GET work item, assert `currentTaskId == "t-1"`. Append a second `step` with `nodeInputs.taskId = "t-2"`, GET again, assert `currentTaskId == "t-2"`. The exact BUG-001 §2 scenario.
- `Get_work_item_assignments_reflects_operator_signal_in_trace` — append an `operator_signal` record with `name == "assignment-confirmed"`, `data.taskId == "t-1"`, `data.payload.assignee == "alice"`. GET work item, assert `executorState.assignments["t-1"] == "alice"`. Append a second signal for `t-1` with assignee `"bob"`, assert last-write-wins. Append a third signal with `name == "tasks-confirmed"` (not assignment-confirmed), assert it does NOT appear in `assignments`.

**Verification Steps:**
1. `dotnet test --nologo`. Expected 177 + new tests, all green.
2. Sanity-check: temporarily revert ONE half of T-094 (e.g. flip `LatestStepTaskId` back to filtering on `kind == "signal"`). Confirm the relevant new test fails. Restore. This is the "would have caught the bug" check.
3. Update `docs/work-items/BUG-001-orchestrator-current-task-id.md` §1 Status: `Resolved`. Append §10 with paths to the two new test files.

---

## Summary

- **Most likely root cause hypothesis:** Confirmed by T-093. DevHub's trace parser was written against an assumed flat shape and a wrong `kind` discriminator; both projections (`currentTaskId` and `assignments`) read it wrong. The FakeOrchestrator harness mirrored DevHub's reader, not the orchestrator's writer, so the bug was invisible to existing tests.
- **Confidence:** Very high. Two independent orchestrator-side integration tests (`test_cli_runs.py:246-256` and `test_lifecycle_anthropic_mocked.py:239`) establish the on-the-wire shape unambiguously.
- **Risk assessment of proposed fix:** Low. Single-repo, no migration, no orchestrator change, no production data impact. The harness realignment causes a one-time cascade of test-fixture updates (5 call sites identified by `grep`), all mechanical.
- **Monitoring recommendations post-fix:** Add a structured-log warning when `FetchStateAsync` produces `currentTaskId == null` while `currentCheckpointKey` indicates a per-task contract — defensive guardrail for the next class of bug in this layer.
- **Related areas to audit for similar issues:** Any future projection that reads the trace (e.g. webhook-event correlation, policy-call summary) MUST read the `{kind, data}` shape. Consider adding a small `TraceRecordReader` helper that exposes `Kind` + a `Data` accessor, used by every projection helper, so future code can't repeat the flat-shape mistake.
- **Process lesson:** Bug reports filed from out-of-repo smoke-test observations should always trigger an investigation task with explicit "verify the schema you THINK you're reading is the one actually on the wire" step. The original BUG-001 was correct in symptom but incomplete in scope; T-093 turned it into a recurring-bug-prevention task.
