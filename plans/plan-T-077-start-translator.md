# Implementation Plan: T-077 — POST /work-items → POST /api/v1/runs

## Task Reference
- **Task ID:** T-077 · **Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-2, AC-8. The entry point for every work item; carries the FEAT-008 `intake.codeSource` block.

## Overview
Translate DevHub's start call into the orchestrator's `CreateRunRequest`, store the marker↔run-id row, and return DevHub's expected `ExecutorStartResponse` shape.

## Implementation Steps

### Step 1: Body translator
**File (sibling):** `src/adapter/translators/start.py` · Create

```python
from typing import Any
from pydantic import BaseModel

class DevHubStartRequest(BaseModel):
    input: dict[str, Any] | None = None
    correlationMarker: str
    intake: dict[str, Any] | None = None  # carries codeSource per FEAT-008

def build_create_run_request(
    body: DevHubStartRequest, agent_ref: str
) -> dict[str, Any]:
    work_item = _extract_work_item(body)
    intake: dict[str, Any] = {"workItem": work_item}
    if body.intake and "codeSource" in body.intake:
        intake["codeSource"] = body.intake["codeSource"]
    return {"agentRef": agent_ref, "intake": intake}

def _extract_work_item(body: DevHubStartRequest) -> dict[str, Any]:
    # If DevHub's input already shapes a work item, pass through.
    if body.input and isinstance(body.input, dict) and "workItem" in body.input:
        return body.input["workItem"]
    # Fallback: synthesize from marker + input as content.
    import json
    return {
        "id": body.correlationMarker,
        "kind": "DEVHUB",
        "content": json.dumps(body.input or {}),
    }
```

### Step 2: Route handler
**File (sibling):** `src/adapter/routes/start.py` · Create

```python
import uuid
from fastapi import APIRouter, Depends, Header, status
from adapter.auth import require_devhub_auth
from adapter.config import Settings
from adapter.store import store
from adapter.translators.start import DevHubStartRequest, build_create_run_request
from adapter.upstream import proxy_call

router = APIRouter(dependencies=[Depends(require_devhub_auth)])
_settings = Settings()

@router.post("/work-items", status_code=status.HTTP_200_OK)
async def start(
    body: DevHubStartRequest,
    x_devhub_executor_id: str | None = Header(default=None, alias="X-DevHub-Executor-Id"),
) -> dict:
    payload = build_create_run_request(body, _settings.agent_ref)
    resp = await proxy_call("POST", "/api/v1/runs", json=payload)
    # Orchestrator returns Envelope[RunSummaryDto] → { data: { id, agentRef, status, ... }, meta: ... }
    run_id = uuid.UUID(resp["data"]["id"])
    executor_id = uuid.UUID(x_devhub_executor_id) if x_devhub_executor_id else uuid.UUID(int=0)
    await store(body.correlationMarker, run_id, executor_id, _settings.agent_ref)
    return {
        "currentStatus": "Running",
        "currentCheckpointKey": None,
        "executorState": {
            "runId": str(run_id),
            "agentRef": _settings.agent_ref,
        },
        "currentTaskId": None,
    }
```

### Step 3: Register the router
**File (sibling):** `src/adapter/main.py` · Modify

```python
from adapter.routes import start as start_routes
app.include_router(start_routes.router)
```

### Step 4: Verify against a live orchestrator
With the umbrella up (orchestrator + adapter both running):

```bash
curl -X POST http://127.0.0.1:8095/work-items \
  -H "Authorization: Bearer <DEVHUB_API_KEY>" \
  -H "Content-Type: application/json" \
  -d '{"input": {}, "correlationMarker": "abc123def456"}'
```

Expect `{"currentStatus":"Running","currentCheckpointKey":null,"executorState":{"runId":"...","agentRef":"lifecycle-agent@0.4.0-manual"},"currentTaskId":null}`. Then check the orchestrator has a new run via `curl http://127.0.0.1:8090/api/v1/runs -H "X-API-Key: <KEY>"`.

## Files Affected
| File | Action | Repo |
|------|--------|------|
| `src/adapter/translators/start.py` | Create | adapter |
| `src/adapter/routes/start.py` | Create | adapter |
| `src/adapter/routes/__init__.py` | Create (empty) | adapter |
| `src/adapter/translators/__init__.py` | Create (empty) | adapter |
| `src/adapter/main.py` | Modify (router registration) | adapter |

## Edge Cases & Risks
- **DevHub doesn't currently send `X-DevHub-Executor-Id`.** Plan defaults to `UUID(int=0)` so the column has a value; if DevHub later adds the header (FEAT-011-ish?), the lookup-by-executor-id index becomes useful. Not a blocker for v1.
- **Marker collision on retry.** Adapter doesn't dedupe. Same risk noted in the brief. Documented; not v1's job.
- **Orchestrator returns 4xx before the row is stored.** Correct — the `store(...)` call lives after `proxy_call`'s success path.
- **`intake.workItem` synthesis when input is a complex shape.** v1 stringifies the whole input into `content`. If the orchestrator validates content size > 1 MB it'll 413; DevHub today doesn't send big inputs. Document.

## Acceptance Verification
- [ ] Curl-based start produces a new run on the orchestrator.
- [ ] Marker mapping row exists after a successful start.
- [ ] DevHub's `ExecutorStartResponse` shape returned exactly.
- [ ] 4xx from orchestrator does not store a marker.
- [ ] `intake.codeSource` from DevHub appears in the orchestrator's stored `RunDetailDto.intake.codeSource`.
