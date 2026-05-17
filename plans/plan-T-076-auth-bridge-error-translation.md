# Implementation Plan: T-076 — Auth bridge + RFC 7807 error pass-through

## Task Reference
- **Task ID:** T-076 · **Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-9. Every endpoint downstream needs auth + a typed upstream client.

## Overview
Inbound bearer validation, outbound `X-API-Key` injection on every upstream call, and a small error type that preserves the orchestrator's RFC 7807 body. All other endpoints depend on this scaffolding.

## Implementation Steps

### Step 1: Inbound auth dependency
**File (sibling):** `src/adapter/auth.py` · Create

```python
from fastapi import Header, HTTPException, status
from adapter.config import Settings

_settings = Settings()

async def require_devhub_auth(authorization: str | None = Header(default=None)) -> None:
    if authorization is None or not authorization.startswith("Bearer "):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={
                "type": "/probs/unauthorized",
                "title": "Missing or malformed bearer token",
                "status": 401,
            },
        )
    token = authorization[len("Bearer "):].strip()
    if token != _settings.devhub_api_key:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={
                "type": "/probs/unauthorized",
                "title": "Invalid bearer token",
                "status": 401,
            },
        )
```

### Step 2: Upstream client + error type
**File (sibling):** `src/adapter/errors.py` · Create

```python
from dataclasses import dataclass
from typing import Any

@dataclass
class UpstreamError(Exception):
    status_code: int
    body: dict[str, Any]
    correlation_id: str | None = None

    def to_response_body(self) -> dict[str, Any]:
        if self.body:
            return self.body
        # Fallback when the orchestrator returned a non-problem-detail 5xx.
        return {
            "type": "/probs/upstream-failure",
            "title": "Upstream returned an error",
            "status": self.status_code,
            "details": {"upstream_correlation_id": self.correlation_id},
        }
```

**File (sibling):** `src/adapter/upstream.py` · Create

```python
import httpx
from adapter.config import Settings
from adapter.errors import UpstreamError

_settings = Settings()

_client = httpx.AsyncClient(
    base_url=_settings.orchestrator_base_url,
    headers={"X-API-Key": _settings.orchestrator_api_key},
    timeout=httpx.Timeout(60.0, read=60.0),
)

async def proxy_call(method: str, path: str, *, json: dict | None = None, params: dict | None = None) -> dict:
    """Forward to the orchestrator; raise UpstreamError on non-2xx."""
    r = await _client.request(method, path, json=json, params=params)
    if 200 <= r.status_code < 300:
        if r.status_code == 204:
            return {}
        return r.json()
    body = {}
    try:
        body = r.json()
    except Exception:
        pass
    raise UpstreamError(
        status_code=r.status_code,
        body=body,
        correlation_id=r.headers.get("x-correlation-id"),
    )

def streaming_client() -> httpx.AsyncClient:
    """Long-lived client for the trace stream — same auth, longer read timeout."""
    return httpx.AsyncClient(
        base_url=_settings.orchestrator_base_url,
        headers={"X-API-Key": _settings.orchestrator_api_key},
        timeout=httpx.Timeout(60.0, read=None),  # no read timeout for SSE
    )
```

### Step 3: Global error handler
**File (sibling):** `src/adapter/main.py` · Modify

Register:

```python
from fastapi import Request
from fastapi.responses import JSONResponse
from adapter.errors import UpstreamError

@app.exception_handler(UpstreamError)
async def upstream_error_handler(request: Request, exc: UpstreamError):
    return JSONResponse(
        status_code=exc.status_code if exc.status_code >= 400 else 502,
        content=exc.to_response_body(),
        media_type="application/problem+json",
    )
```

The status normalization: orchestrator 4xx pass through with original code; an unexpected 5xx that DevHub interprets as `ExecutorFailureException` → 502 is the right thing. We preserve the orchestrator's status verbatim when ≥ 400 (so 409/404 etc. pass through to DevHub correctly).

### Step 4: Inbound auth on /health is exempt
**File (sibling):** `src/adapter/main.py` · Modify

`/health` doesn't require auth (umbrella health checks need to reach it before DevHub does). Mark only the protocol-translation routes with `dependencies=[Depends(require_devhub_auth)]` (T-077 onward).

### Step 5: Quick unit tests
**File (sibling):** `tests/test_auth.py` · Create

- Missing `Authorization` → 401 with RFC 7807 body.
- `Authorization: Bearer wrong` → 401.
- `Authorization: Bearer <correct>` → 200 on a stubbed route.

**File (sibling):** `tests/test_upstream.py` · Create — uses `respx` to mock the orchestrator:

- 200 OK → returns parsed JSON.
- 404 with RFC 7807 body → raises `UpstreamError(404, body=...)`.
- 500 with no body → raises `UpstreamError(500, body={})` with `correlation_id` from `x-correlation-id` header when present.

## Files Affected
| File | Action | Repo |
|------|--------|------|
| `src/adapter/auth.py` | Create | adapter |
| `src/adapter/errors.py` | Create | adapter |
| `src/adapter/upstream.py` | Create | adapter |
| `src/adapter/main.py` | Modify | adapter |
| `tests/test_auth.py` | Create | adapter |
| `tests/test_upstream.py` | Create | adapter |

## Edge Cases & Risks
- **Orchestrator returns 401 itself.** Adapter forwards the 401; DevHub sees its outbound key was rejected. This shouldn't happen if the keys are configured correctly but the path is honest.
- **Network timeout.** `httpx.TimeoutException` is not caught here; the FastAPI default handler returns 500. T-082's tests confirm DevHub's `ExecutorFailureException` handles this end-to-end via the 502 mapping.
- **Client connection pool.** A single `httpx.AsyncClient` is shared by every endpoint (reuse connections). The streaming client is opened per-request and closed when the SSE client disconnects.

## Acceptance Verification
- [ ] Inbound bearer validation works (401 on missing/wrong; 200 on correct).
- [ ] Upstream errors pass through with original status + body.
- [ ] 5xx without a body produces a synthesized RFC 7807 body.
- [ ] `/health` is exempt from auth.
