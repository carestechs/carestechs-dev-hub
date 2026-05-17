# Implementation Plan: T-081 — Stream translator (NDJSON → SSE)

## Task Reference
- **Task ID:** T-081 · **Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** L
- **Rationale:** AC-5. Live-trace SSE pass-through is what makes the lifecycle review screen feel native; this translator is the hot path.

## Overview
Long-lived HTTP connection to the orchestrator's `/trace?follow=true`. Each NDJSON line → one SSE `data: <json>\n\n` frame. Pre-flight 404 if the marker doesn't exist; client disconnect closes upstream within 1 s.

## Implementation Steps

### Step 1: Stream translator generator
**File (sibling):** `src/adapter/translators/stream.py` · Create

```python
from typing import AsyncIterator
from adapter.upstream import streaming_client

async def ndjson_to_sse(run_id: str) -> AsyncIterator[bytes]:
    """Open the orchestrator's trace stream and convert each NDJSON line to an SSE frame."""
    # Initial heartbeat so DevHub's EventSource fires onopen quickly.
    yield b": ready\n\n"

    async with streaming_client() as client:
        async with client.stream(
            "GET",
            f"/api/v1/runs/{run_id}/trace",
            params={"follow": "true"},
        ) as r:
            # Upstream 404 — surface to caller via raise (handled by the route).
            if r.status_code == 404:
                # Read body once before raising so the route can wrap it.
                body = await r.aread()
                from adapter.errors import UpstreamError
                import json as _json
                try:
                    parsed = _json.loads(body)
                except Exception:
                    parsed = {}
                raise UpstreamError(404, parsed)
            r.raise_for_status()

            async for line in r.aiter_lines():
                if not line or not line.strip():
                    continue
                # Validate JSON shape before forwarding; suppress malformed.
                try:
                    import json as _json
                    _json.loads(line)
                except Exception:
                    continue
                yield f"data: {line}\n\n".encode("utf-8")
```

### Step 2: Stream route
**File (sibling):** `src/adapter/routes/stream.py` · Create

```python
from fastapi import APIRouter, Depends, HTTPException
from fastapi.responses import StreamingResponse
from adapter.auth import require_devhub_auth
from adapter.store import lookup_run_id
from adapter.translators.stream import ndjson_to_sse

router = APIRouter(dependencies=[Depends(require_devhub_auth)])

@router.get("/work-items/{marker}/stream")
async def stream(marker: str) -> StreamingResponse:
    run_id = await lookup_run_id(marker)
    if run_id is None:
        raise HTTPException(
            status_code=404,
            detail={"type": "/probs/not-found", "title": "Work item not found", "status": 404},
        )
    return StreamingResponse(
        ndjson_to_sse(str(run_id)),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "X-Accel-Buffering": "no",
            "Connection": "keep-alive",
        },
    )
```

### Step 3: Register
**File (sibling):** `src/adapter/main.py` · Modify

```python
from adapter.routes import stream as stream_routes
app.include_router(stream_routes.router)
```

### Step 4: Smoke
```bash
curl -N http://127.0.0.1:8095/work-items/<marker>/stream \
  -H "Authorization: Bearer <DEVHUB_API_KEY>"
```

Expect `: ready` followed by `data: { ... }` frames as the orchestrator emits trace records. `Ctrl-C` closes the connection; the adapter should release the upstream connection within 1 s.

## Files Affected
| File | Action | Repo |
|------|--------|------|
| `src/adapter/translators/stream.py` | Create | adapter |
| `src/adapter/routes/stream.py` | Create | adapter |
| `src/adapter/main.py` | Modify (router) | adapter |

## Edge Cases & Risks
- **Client disconnect handling.** FastAPI's `StreamingResponse` cancels the generator when the client closes; `httpx`'s `client.stream(...)` context manager closes the upstream socket on `__aexit__`. Verified by `await r.aclose()` semantics. No explicit cancel needed.
- **Malformed NDJSON lines.** Suppressed silently with a log line; SSE consumers never see them. Log at WARN level (not ERROR — these don't break the stream).
- **Reconnect storms.** If DevHub's EventSource reconnects rapidly (network flap), each reconnect opens a fresh upstream stream. Orchestrator might enforce per-IP rate limits; v1 doesn't add any rate-limit handling on the adapter side. Acceptable.
- **NDJSON line size.** The orchestrator can emit large trace records (e.g. full policy-call payloads). SSE has no enforced line size, but some proxies do. `X-Accel-Buffering: no` covers nginx. Document.
- **Long pause without activity.** The orchestrator's `follow=true` keeps the connection open during paused runs; no heartbeat emitted on the upstream side. The adapter relies on TCP keepalive; if connections drop after idle, DevHub reconnects via EventSource auto-retry. No application-level heartbeat in v1.

## Acceptance Verification
- [ ] `: ready\n\n` heartbeat emitted within 100 ms of connection open.
- [ ] One NDJSON line in → one SSE frame out.
- [ ] Empty / whitespace lines suppressed.
- [ ] Malformed JSON suppressed (log shows warning).
- [ ] Pre-flight 404 before any body bytes when marker unknown.
- [ ] Client `Ctrl-C` closes upstream connection within 1 s.
