# Implementation Plan: T-078 — GET /work-items/{marker} → /api/v1/runs/{run_id} + state derivation

## Task Reference
- **Task ID:** T-078 · **Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** L
- **Rationale:** AC-3, AC-7. The hardest task — `currentCheckpointKey` + `currentTaskId` + `executorState.assignments` all need derivation since the orchestrator's `RunDetailDto` doesn't expose them directly.

## Overview
Three derivations stacked. Pick the marker → fetch the orchestrator's run → map status → derive checkpoint key + task id from a trace scan → assemble `executorState` including assignments from a replay of `assignment-confirmed` signals → return DevHub's `ExecutorFetchResponse` shape.

## Implementation Steps

### Step 1: Status mapping
**File (sibling):** `src/adapter/translators/state.py` · Create

```python
_RUN_STATUS_MAP = {
    "pending": "Running",
    "running": "Running",
    "paused": "WaitingOnCheckpoint",
    "completed": "Completed",
    "failed": "Failed",
    "cancelled": "Cancelled",
}

def map_run_status(orchestrator_status: str) -> str:
    return _RUN_STATUS_MAP.get(orchestrator_status, "Running")
```

### Step 2: Trace scanner
**File (sibling):** `src/adapter/translators/trace_scan.py` · Create

Async generator that streams NDJSON from `/api/v1/runs/{run_id}/trace` (no `follow`), parses each line, and returns records.

```python
from typing import Any, AsyncIterator
import json
from adapter.upstream import streaming_client

async def scan_trace(run_id: str, kinds: list[str] | None = None) -> AsyncIterator[dict[str, Any]]:
    params = {}
    if kinds:
        params["kind"] = kinds  # repeatable query param
    async with streaming_client() as client:
        async with client.stream("GET", f"/api/v1/runs/{run_id}/trace", params=params) as r:
            r.raise_for_status()
            async for line in r.aiter_lines():
                if not line.strip():
                    continue
                try:
                    yield json.loads(line)
                except json.JSONDecodeError:
                    continue
```

### Step 3: Derive currentCheckpointKey + currentTaskId
**File (sibling):** `src/adapter/translators/state.py` · Modify (extend)

```python
from typing import Any
from adapter.translators.trace_scan import scan_trace

async def derive_checkpoint_and_task(run_id: str, last_step: dict | None) -> tuple[str | None, str | None]:
    # Tier 1: last step's node_inputs.awaiting_signal (if the agent uses that key).
    if last_step:
        inputs = last_step.get("nodeInputs") or {}
        signal_name = inputs.get("awaiting_signal")
        task_id = inputs.get("current_task_id")
        if signal_name:
            return signal_name, task_id

    # Tier 2: trace scan for the most recent awaiting_signal record.
    latest_await: dict | None = None
    async for rec in scan_trace(run_id, kinds=["awaiting_signal"]):
        latest_await = rec  # records are streamed in order; keep the last
    if latest_await:
        return (
            latest_await.get("name") or latest_await.get("signalName"),
            latest_await.get("taskId") or latest_await.get("current_task_id"),
        )

    # Tier 3: null.
    return None, None
```

### Step 4: Derive assignments from trace
**File (sibling):** `src/adapter/translators/state.py` · Modify (extend)

```python
async def derive_assignments(run_id: str) -> dict[str, str]:
    """Replay every `assignment-confirmed` signal record from the trace."""
    out: dict[str, str] = {}
    async for rec in scan_trace(run_id, kinds=["signal"]):
        if rec.get("name") != "assignment-confirmed":
            continue
        payload = rec.get("payload") or {}
        assignee = payload.get("assignee")
        task_id = rec.get("taskId") or payload.get("taskId")
        if task_id and isinstance(assignee, str) and assignee:
            out[task_id] = assignee
    return out
```

### Step 5: Assemble executorState
**File (sibling):** `src/adapter/translators/state.py` · Modify (extend)

```python
async def assemble_executor_state(run_id: str, run_detail: dict, agent_ref: str) -> dict:
    assignments = await derive_assignments(run_id)
    last_step = run_detail.get("lastStep")
    return {
        "runId": run_id,
        "agentRef": agent_ref,
        "lastStep": last_step,
        "assignments": assignments,
        "stopReason": run_detail.get("stopReason"),
    }
```

### Step 6: Fetch route
**File (sibling):** `src/adapter/routes/fetch.py` · Create

```python
from fastapi import APIRouter, Depends, HTTPException
from adapter.auth import require_devhub_auth
from adapter.config import Settings
from adapter.store import lookup_run_id
from adapter.translators.state import (
    map_run_status, derive_checkpoint_and_task, assemble_executor_state,
)
from adapter.upstream import proxy_call

router = APIRouter(dependencies=[Depends(require_devhub_auth)])
_settings = Settings()

@router.get("/work-items/{marker}")
async def fetch(marker: str) -> dict:
    run_id = await lookup_run_id(marker)
    if run_id is None:
        raise HTTPException(
            status_code=404,
            detail={"type": "/probs/not-found", "title": "Work item not found", "status": 404},
        )
    resp = await proxy_call("GET", f"/api/v1/runs/{run_id}")
    detail = resp["data"]

    status_str = map_run_status(detail["status"])
    if status_str == "WaitingOnCheckpoint":
        checkpoint_key, current_task_id = await derive_checkpoint_and_task(str(run_id), detail.get("lastStep"))
    else:
        checkpoint_key, current_task_id = None, None

    return {
        "currentStatus": status_str,
        "currentCheckpointKey": checkpoint_key,
        "executorState": await assemble_executor_state(str(run_id), detail, _settings.agent_ref),
        "currentTaskId": current_task_id,
    }
```

### Step 7: Register the route
**File (sibling):** `src/adapter/main.py` · Modify

```python
from adapter.routes import fetch as fetch_routes
app.include_router(fetch_routes.router)
```

### Step 8: Manual smoke
```bash
# After starting a work item (T-077 verified):
curl http://127.0.0.1:8095/work-items/<marker> \
  -H "Authorization: Bearer <DEVHUB_API_KEY>"
```

Expect the shape DevHub expects (`currentStatus`, `currentCheckpointKey`, `executorState`, `currentTaskId`).

## Files Affected
| File | Action | Repo |
|------|--------|------|
| `src/adapter/translators/state.py` | Create | adapter |
| `src/adapter/translators/trace_scan.py` | Create | adapter |
| `src/adapter/routes/fetch.py` | Create | adapter |
| `src/adapter/main.py` | Modify (router) | adapter |

## Edge Cases & Risks
- **Tier 1 awaiting_signal field name unverified.** I'm guessing the agent definition writes `node_inputs.awaiting_signal`. Before merging, grep the agent definitions in `../carestechs-agent-orchestrator/agents/` for the actual key name. If it's different (e.g. `expected_signal`), update the dict key.
- **Trace scan cost.** Each `GET /work-items/{marker}` does one to two trace scans. For long-running runs with many signals this can be O(N). v1 acceptable; if a bottleneck appears, add a 60s in-process LRU cache keyed by `run_id`.
- **`signal` records may include other signal names.** The assignments filter checks `name == "assignment-confirmed"` so other signal types pass through harmlessly.
- **Trace stream backpressure.** `aiter_lines` is bounded by Python's read buffer; for trace dumps under a few MB this is fine. For multi-MB traces, the scan finishes in a couple of seconds — acceptable for a fetch path.

## Acceptance Verification
- [ ] Status mapping covers all six `RunStatus` values.
- [ ] `currentCheckpointKey` derived from tier 1 when present; tier 2 when not; null otherwise.
- [ ] `currentTaskId` derived from the same records.
- [ ] `executorState.assignments` populated from `assignment-confirmed` signal replays.
- [ ] 404 from marker lookup before any upstream call.
- [ ] Manual smoke against a paused run returns `WaitingOnCheckpoint` + the right checkpoint key.
