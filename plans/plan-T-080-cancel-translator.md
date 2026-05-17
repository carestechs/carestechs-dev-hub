# Implementation Plan: T-080 — Cancel translator

## Task Reference
- **Task ID:** T-080 · **Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-6. Closes the lifecycle from DevHub UI.

## Overview
Simplest translation. Marker → run_id → POST orchestrator's `/cancel` → return 204.

## Implementation Steps

### Step 1: Route
**File (sibling):** `src/adapter/routes/cancel.py` · Create

```python
from fastapi import APIRouter, Depends, HTTPException, Response
from adapter.auth import require_devhub_auth
from adapter.store import lookup_run_id
from adapter.upstream import proxy_call

router = APIRouter(dependencies=[Depends(require_devhub_auth)])

@router.post("/work-items/{marker}/cancel", status_code=204)
async def cancel(marker: str) -> Response:
    run_id = await lookup_run_id(marker)
    if run_id is None:
        raise HTTPException(
            status_code=404,
            detail={"type": "/probs/not-found", "title": "Work item not found", "status": 404},
        )
    # Orchestrator's CancelRunRequest body has an optional `reason`; default works in v1.
    await proxy_call("POST", f"/api/v1/runs/{run_id}/cancel", json={"reason": "DevHub operator cancel"})
    return Response(status_code=204)
```

### Step 2: Register
**File (sibling):** `src/adapter/main.py` · Modify

```python
from adapter.routes import cancel as cancel_routes
app.include_router(cancel_routes.router)
```

### Step 3: Manual smoke
```bash
curl -X POST http://127.0.0.1:8095/work-items/<marker>/cancel \
  -H "Authorization: Bearer <DEVHUB_API_KEY>"
# Expect 204.
```

## Files Affected
| File | Action | Repo |
|------|--------|------|
| `src/adapter/routes/cancel.py` | Create | adapter |
| `src/adapter/main.py` | Modify (router) | adapter |

## Edge Cases & Risks
- **Cancel on an already-terminal run.** Orchestrator returns 409 or 410 (need to confirm exact code); the `UpstreamError` handler passes it through.
- **DevHub's CancelAsync expects 204.** Verified against the C# client; matches.
- **`CancelRunRequest.reason` field.** If the orchestrator's schema is stricter (required, enum-constrained), the proxy_call returns 422 with the validation error. v1 hardcodes a single string; if that's rejected, drop the body to `{}` and rerun the smoke.

## Acceptance Verification
- [ ] Cancel on a running work item → 204 from adapter; orchestrator's run becomes `cancelled`.
- [ ] Cancel on unknown marker → 404, no upstream call.
- [ ] Cancel on a terminal run → upstream's status code (likely 409) passes through.
