# Implementation Plan: T-079 — Signal translator

## Task Reference
- **Task ID:** T-079 · **Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-4, AC-7. The operator's path for every checkpoint signal — including FEAT-009 `assignment-confirmed`.

## Overview
Marker → run_id → POST to orchestrator's `/api/v1/runs/{run_id}/signals`. Re-fetch run state on the response. The orchestrator's `name` field is what DevHub calls `checkpointKey` — straight mapping.

## Implementation Steps

### Step 1: Shared state-refresh helper
**File (sibling):** `src/adapter/translators/state.py` · Modify (extend)

Extract a helper used by both fetch and signal:

```python
async def fetch_run_state_for_devhub(run_id: str, agent_ref: str) -> dict:
    resp = await proxy_call("GET", f"/api/v1/runs/{run_id}")
    detail = resp["data"]
    status_str = map_run_status(detail["status"])
    if status_str == "WaitingOnCheckpoint":
        ck, ct = await derive_checkpoint_and_task(run_id, detail.get("lastStep"))
    else:
        ck, ct = None, None
    return {
        "currentStatus": status_str,
        "currentCheckpointKey": ck,
        "executorState": await assemble_executor_state(run_id, detail, agent_ref),
        "currentTaskId": ct,
    }
```

Refactor `fetch.py` to use this helper.

### Step 2: Signal route
**File (sibling):** `src/adapter/routes/signal.py` · Create

```python
import uuid
from typing import Any
from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel
from adapter.auth import require_devhub_auth
from adapter.config import Settings
from adapter.store import lookup_run_id
from adapter.translators.state import fetch_run_state_for_devhub
from adapter.upstream import proxy_call

router = APIRouter(dependencies=[Depends(require_devhub_auth)])
_settings = Settings()

class DevHubSignalRequest(BaseModel):
    outcome: str
    payload: dict[str, Any] | None = None
    taskId: str | None = None

@router.post("/work-items/{marker}/checkpoints/{checkpoint_key}/signal")
async def signal(
    marker: str,
    checkpoint_key: str,
    body: DevHubSignalRequest,
) -> dict:
    run_id = await lookup_run_id(marker)
    if run_id is None:
        raise HTTPException(
            status_code=404,
            detail={"type": "/probs/not-found", "title": "Work item not found", "status": 404},
        )

    # outcome is DevHub-side bookkeeping; the orchestrator routes by signal name.
    orchestrator_body = {
        "name": checkpoint_key,
        "taskId": body.taskId,
        "payload": body.payload or {},
    }
    orch_resp = await proxy_call("POST", f"/api/v1/runs/{run_id}/signals", json=orchestrator_body)
    # SignalCreateResponse: { data: SignalDto, meta: { alreadyReceived: bool } | null }
    http_status = 200  # orchestrator returns 202, DevHub expects 200 with body — adapter normalizes

    # Refresh state so DevHub gets the new currentStatus / currentCheckpointKey / currentTaskId.
    state = await fetch_run_state_for_devhub(str(run_id), _settings.agent_ref)
    state["httpStatus"] = http_status
    return state
```

### Step 3: Register route
**File (sibling):** `src/adapter/main.py` · Modify

```python
from adapter.routes import signal as signal_routes
app.include_router(signal_routes.router)
```

### Step 4: Manual smoke
```bash
# After a work item parks on assignment-confirmed (T-077 + T-078 verified):
curl -X POST http://127.0.0.1:8095/work-items/<marker>/checkpoints/assignment-confirmed/signal \
  -H "Authorization: Bearer <DEVHUB_API_KEY>" \
  -H "Content-Type: application/json" \
  -d '{"outcome": "confirmed", "payload": {"assignee": "Alice"}, "taskId": "T-001"}'
```

Expect a refreshed state body with the new `currentTaskId` or moved-past checkpoint.

## Files Affected
| File | Action | Repo |
|------|--------|------|
| `src/adapter/translators/state.py` | Modify | adapter |
| `src/adapter/routes/fetch.py` | Modify (refactor to use the shared helper) | adapter |
| `src/adapter/routes/signal.py` | Create | adapter |
| `src/adapter/main.py` | Modify (router) | adapter |

## Edge Cases & Risks
- **Orchestrator's `meta.alreadyReceived` flag.** Adapter discards it (DevHub doesn't have a representation for "duplicate signal"). The signal still ends up dedup'd on the orchestrator side; DevHub gets the same `state` body either way. Documented in the signal translator's docstring.
- **`outcome` field unused.** DevHub historically uses `outcome` as a UI label (`approve` / `reject` / `confirmed` / etc.); the orchestrator routes by `name`. The adapter ignores `outcome` — but `checkpoint_key` IS the orchestrator's `name`, so the round-trip is consistent.
- **Race between signal and state refresh.** The orchestrator processes the signal asynchronously; the immediate `GET /runs/{run_id}` may still show the pre-signal state. Acceptable — DevHub polls via the reconciler; the next fetch picks up the new state. Not v1's problem.

## Acceptance Verification
- [ ] Signal forwards `name = checkpointKey`, `taskId`, `payload` verbatim.
- [ ] Response body matches DevHub's `ExecutorSignalResponse` shape including `httpStatus`.
- [ ] State refresh runs after the signal so DevHub sees up-to-date checkpoint info.
- [ ] 404 on unknown marker, no upstream call.
