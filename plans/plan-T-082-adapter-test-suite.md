# Implementation Plan: T-082 — Adapter test suite

## Task Reference
- **Task ID:** T-082 · **Type:** Testing (adapter) · **Workflow:** standard · **Complexity:** M
- **Rationale:** The brief's quality bar. Without coverage, the next refactor breaks something silently.

## Overview
`pytest` + `respx` (httpx mock library) to cover every translation path. Unit tests for the four pure-translation modules (status mapping, checkpoint derivation, assignments replay, executor-state assembly). Integration tests through FastAPI's `TestClient` with `respx` mocking the orchestrator.

## Implementation Steps

### Step 1: Test fixtures
**File (sibling):** `tests/conftest.py` · Create

```python
import os
import pytest
from fastapi.testclient import TestClient

# Set env BEFORE importing the app so Settings() resolves cleanly.
os.environ.setdefault("DEVHUB_API_KEY", "test-devhub-key")
os.environ.setdefault("ORCHESTRATOR_BASE_URL", "http://test-orchestrator")
os.environ.setdefault("ORCHESTRATOR_API_KEY", "test-orch-key")
os.environ.setdefault("AGENT_REF", "lifecycle-agent@0.4.0-manual")
os.environ.setdefault("ADAPTER_DB_URL", "sqlite+aiosqlite:///:memory:")

@pytest.fixture(scope="session")
def auth_headers() -> dict[str, str]:
    return {"Authorization": "Bearer test-devhub-key"}

@pytest.fixture
def client():
    from adapter.main import app
    with TestClient(app) as c:
        yield c
```

For SQLite in-memory the model needs `Base.metadata.create_all` on startup (Alembic isn't used in tests). Add a startup-event hook gated on `ADAPTER_DB_URL.startswith("sqlite")`.

### Step 2: Status-mapping unit tests
**File (sibling):** `tests/test_status_mapping.py` · Create

```python
import pytest
from adapter.translators.state import map_run_status

@pytest.mark.parametrize("orch,expected", [
    ("pending", "Running"),
    ("running", "Running"),
    ("paused", "WaitingOnCheckpoint"),
    ("completed", "Completed"),
    ("failed", "Failed"),
    ("cancelled", "Cancelled"),
    ("unknown-value", "Running"),  # safe default
])
def test_map_run_status(orch, expected):
    assert map_run_status(orch) == expected
```

### Step 3: Checkpoint-derivation tests
**File (sibling):** `tests/test_checkpoint_derivation.py` · Create

Three test functions, one per tier:
1. Tier 1: `last_step` has `nodeInputs.awaiting_signal` and `current_task_id` → returns both.
2. Tier 2: `last_step` is None / no awaiting_signal → respx-mocks the trace endpoint with a single `awaiting_signal` record → returns derived values.
3. Tier 3: no last_step + empty trace → returns `(None, None)`.

### Step 4: Assignments-replay tests
**File (sibling):** `tests/test_executor_state.py` · Create

- Three `assignment-confirmed` signal records in the trace → all three appear in the assembled `assignments` map.
- Mixed signal records (other signal names) → only `assignment-confirmed` entries appear.
- No signal records → empty `{}`.
- Records with missing `taskId` or missing `assignee` filtered out.
- Records with non-string `assignee` filtered out.

### Step 5: Start route test
**File (sibling):** `tests/test_start.py` · Create

```python
import respx
from httpx import Response

@respx.mock
def test_start_forwards_intake_and_stores_marker(client, auth_headers):
    respx.post("http://test-orchestrator/api/v1/runs").mock(
        return_value=Response(202, json={
            "data": {"id": "00000000-0000-0000-0000-000000000001", "agentRef": "...", "status": "running", "startedAt": "..."},
            "meta": None,
        }),
    )
    r = client.post("/work-items", json={
        "input": {},
        "correlationMarker": "abc123",
        "intake": {"codeSource": {"repo": "carestechs/x", "baseBranch": "main"}},
    }, headers=auth_headers)
    assert r.status_code == 200
    body = r.json()
    assert body["currentStatus"] == "Running"
    assert body["currentCheckpointKey"] is None
    assert body["executorState"]["runId"] == "00000000-0000-0000-0000-000000000001"

    # Inspect the upstream request to verify intake.codeSource passed through.
    sent = respx.calls.last.request
    import json
    sent_body = json.loads(sent.content)
    assert sent_body["intake"]["codeSource"]["repo"] == "carestechs/x"
    assert sent_body["agentRef"] == "lifecycle-agent@0.4.0-manual"
```

### Step 6: Signal route test
**File (sibling):** `tests/test_signal.py` · Create

- Marker not found → 404, no upstream call.
- Marker found → POSTs `/api/v1/runs/{id}/signals` with `{ name, taskId, payload }` matching DevHub's input.
- Refresh-state path produces a `currentStatus` + `currentCheckpointKey` in the response.
- Orchestrator's `meta.alreadyReceived` flag handled (response shape unchanged).

### Step 7: Stream route test
**File (sibling):** `tests/test_stream.py` · Create

- Marker not found → 404 before any upstream call (verified via respx call count == 0).
- Stream emits `: ready\n\n` first.
- Each NDJSON line in the upstream stream → one `data: ...\n\n` frame out.
- Malformed JSON line suppressed (verified by line-count mismatch).
- Empty / whitespace lines suppressed.

The `httpx` MockTransport for streams is a bit awkward — use `respx`'s `stream` support or fall back to a fakehttpx transport. Sample test in `respx` docs.

### Step 8: Run + assert
```bash
cd ../carestechs-devhub-orchestrator-adapter
pip install -e ".[dev]"
pytest -v
```

Expect ≥ 25 tests green.

## Files Affected
| File | Action | Repo |
|------|--------|------|
| `tests/conftest.py` | Create | adapter |
| `tests/test_auth.py` | Already from T-076 | adapter |
| `tests/test_upstream.py` | Already from T-076 | adapter |
| `tests/test_status_mapping.py` | Create | adapter |
| `tests/test_checkpoint_derivation.py` | Create | adapter |
| `tests/test_executor_state.py` | Create | adapter |
| `tests/test_start.py` | Create | adapter |
| `tests/test_signal.py` | Create | adapter |
| `tests/test_stream.py` | Create | adapter |

## Edge Cases & Risks
- **Startup-time DB migration in tests.** Tests use SQLite in-memory; Alembic's Postgres-specific DDL won't run. Use `Base.metadata.create_all()` via a startup-event hook gated on `sqlite` in the URL. Document.
- **`respx` for streaming.** Documented support exists but is less common; if `respx.stream` proves flaky, fall back to a custom `httpx.MockTransport`.
- **Test speed.** Aim for the suite to complete in under 10 s. No network, no Postgres, no real orchestrator.
- **Coverage holes.** Cancel route is trivial (T-080) and tested via a single happy-path + 404 case.

## Acceptance Verification
- [ ] ≥ 25 tests pass.
- [ ] Coverage report shows ≥ 80% on every `translators/*.py` and `routes/*.py` file.
- [ ] Test suite runs in < 10 s.
