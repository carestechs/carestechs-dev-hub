# Implementation Plan: T-089 — ARCHITECTURE.md + api-spec.md + FEAT-010 brief Status

## Task Reference
- **Task ID:** T-089 · **Type:** Documentation · **Workflow:** standard · **Complexity:** S
- **Rationale:** Documentation closes FEAT-010. Operators need to know which protocol to choose and where to look when debugging.

## Overview
Three docs touched. Mark the brief Completed.

## Implementation Steps

### Step 1: ARCHITECTURE.md — new section
**File:** `docs/ARCHITECTURE.md` · Modify

Under the Executor Registry section (or wherever protocol-level decisions live), add:

```markdown
### Executor protocols (FEAT-010)

DevHub talks to executors through `IExecutorHttpClient`. Two implementations ship today:

- **`ExecutorHttpClient`** — speaks DevHub's native protocol (`POST /work-items`, `POST /work-items/{marker}/checkpoints/{key}/signal`, etc.). Used by the FakeExecutor in tests and any executor that adopts DevHub's wire shape.
- **`OrchestratorExecutorClient`** — speaks the carestechs-agent-orchestrator's `/api/v1/runs` API. Translates DevHub's calls to the orchestrator's routes; maps `RunStatus` → `CurrentStatus`; derives `currentCheckpointKey` + `currentTaskId` from trace records; converts NDJSON trace to SSE inline.

Selection happens per executor via `ExecutorRegistration.Protocol` (`"devhub"` or `"orchestrator"`; default `"devhub"`). `IExecutorClientFactory.Resolve(descriptor)` returns the right implementation. The `WorkItem` row carries both `ExecutorCorrelationMarker` (DevHub's id) and `ExecutorRunId` (the orchestrator's run id, populated after Start succeeds).

The decision to live in-process — rather than as a sibling adapter service — is recorded in the FEAT-010 brief (§11).
```

Add a changelog entry at the bottom:

```
- **2026-05-17 (FEAT-010)** — Added `OrchestratorExecutorClient` as a second `IExecutorHttpClient` implementation; selection via `ExecutorRegistration.Protocol`. The original "Adapter service" framing was discarded in favor of an in-process class; brief §11 records the reasoning.
```

### Step 2: api-spec.md — note the protocol field
**File:** `docs/api-spec.md` · Modify

In the ExecutorRegistry section near the request body example, add a note about `protocol`. The DTO field tables (T-086 already updated these); this is just a prose pointer. Append a changelog entry covering the changes from T-086 if not already done.

### Step 3: Flip FEAT-010 brief Status
**File:** `docs/work-items/FEAT-010-orchestrator-client.md` · Modify

```markdown
| **Status** | Completed (2026-05-17) |
```

### Step 4: Smoke
**Bash:**

```bash
grep -n "FEAT-010" docs/ARCHITECTURE.md docs/api-spec.md
grep -n "Status.*Completed" docs/work-items/FEAT-010-orchestrator-client.md
```

Each grep finds the changelog / status entries.

## Files Affected
| File | Action |
|---|---|
| `docs/ARCHITECTURE.md` | Modify |
| `docs/api-spec.md` | Modify |
| `docs/work-items/FEAT-010-orchestrator-client.md` | Modify (Status) |

## Edge Cases & Risks
- **Existing docs already mention the adapter service** (from the now-deleted earlier brief). Nothing in this repo references it after this PR's cleanup, but if any merged commit/changelog references `FEAT-010-orchestrator-adapter.md`, leave it alone — historical accuracy matters more than retroactive cleanup. The new content uses the new filename.

## Acceptance Verification
- [ ] ARCHITECTURE.md has the "Executor protocols" section + changelog row.
- [ ] api-spec.md mentions the protocol field next to the executor request shape.
- [ ] FEAT-010 brief Status is Completed.
