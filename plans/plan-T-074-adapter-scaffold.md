# Implementation Plan: T-074 — Adapter repo scaffold + umbrella wiring

## Task Reference
- **Task ID:** T-074 · **Type:** DevOps · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-1, AC-10. Foundation — every other task assumes the project exists.

## Overview
Bootstrap a new sibling repo at `../carestechs-devhub-orchestrator-adapter`. Python 3.12 + FastAPI. `GET /health` checks both adapter liveness and orchestrator reachability. Container joins the umbrella; loopback host port 8095. Single `../start.sh` line added.

## Implementation Steps

### Step 1: Create the repo
**Bash (from `..`):**

```bash
mkdir -p carestechs-devhub-orchestrator-adapter/{src/adapter,tests,alembic/versions,scripts}
cd carestechs-devhub-orchestrator-adapter
git init
```

Add `.gitignore` (Python defaults — `__pycache__/`, `.venv/`, `*.egg-info/`, `.env*`, `dist/`, `build/`, `.pytest_cache/`).

### Step 2: `pyproject.toml`
**File (sibling):** `pyproject.toml` · Create

```toml
[project]
name = "carestechs-devhub-orchestrator-adapter"
version = "0.1.0"
requires-python = ">=3.12"
dependencies = [
  "fastapi>=0.115",
  "uvicorn[standard]>=0.30",
  "httpx>=0.27",
  "sqlalchemy>=2.0",
  "asyncpg>=0.29",
  "pydantic-settings>=2.4",
  "python-dotenv>=1.0",
]

[project.optional-dependencies]
dev = [
  "pytest>=8.3",
  "pytest-asyncio>=0.24",
  "respx>=0.21",
]

[tool.pytest.ini_options]
asyncio_mode = "auto"
```

### Step 3: Minimal FastAPI app
**File (sibling):** `src/adapter/__init__.py` · Create (empty).
**File (sibling):** `src/adapter/config.py` · Create:

```python
from pydantic_settings import BaseSettings, SettingsConfigDict

class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env.production", extra="ignore")
    devhub_api_key: str
    orchestrator_base_url: str
    orchestrator_api_key: str
    agent_ref: str
    adapter_db_url: str
```

**File (sibling):** `src/adapter/main.py` · Create:

```python
from fastapi import FastAPI, status
from fastapi.responses import JSONResponse
import httpx
from adapter.config import Settings

app = FastAPI(title="DevHub Orchestrator Adapter")
settings = Settings()
upstream = httpx.AsyncClient(
    base_url=settings.orchestrator_base_url,
    headers={"X-API-Key": settings.orchestrator_api_key},
    timeout=60.0,
)

@app.get("/health")
async def health() -> JSONResponse:
    try:
        r = await upstream.get("/health", timeout=5.0)
        upstream_ok = r.status_code == 200
    except Exception:
        upstream_ok = False
    status_str = "ok" if upstream_ok else "degraded"
    code = status.HTTP_200_OK if upstream_ok else status.HTTP_503_SERVICE_UNAVAILABLE
    return JSONResponse({"status": status_str, "upstream": upstream_ok}, status_code=code)
```

### Step 4: Dockerfile
**File (sibling):** `Dockerfile` · Create

Multi-stage:

```dockerfile
FROM python:3.12-slim AS builder
WORKDIR /build
COPY pyproject.toml .
RUN pip install --upgrade pip && pip install --prefix=/install .

FROM python:3.12-slim
WORKDIR /app
COPY --from=builder /install /usr/local
COPY src /app/src
ENV PYTHONPATH=/app/src
EXPOSE 8000
CMD ["uvicorn", "adapter.main:app", "--host", "0.0.0.0", "--port", "8000"]
```

### Step 5: docker-compose.prod.yml
**File (sibling):** `docker-compose.prod.yml` · Create

```yaml
services:
  adapter:
    build: .
    container_name: devhub-orchestrator-adapter
    env_file: .env.production
    ports:
      - "127.0.0.1:8095:8000"
    networks: [infra]
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "python", "-c", "import urllib.request; urllib.request.urlopen('http://localhost:8000/health', timeout=5)"]
      interval: 10s
      timeout: 5s
      retries: 3

networks:
  infra:
    external: true
    name: devtools-infra
```

### Step 6: `.env.production.example`
**File (sibling):** `.env.production.example` · Create

```dotenv
# Inbound auth — DevHub forwards this as Bearer.
DEVHUB_API_KEY=replace-me-with-shared-secret

# Outbound: the orchestrator's HTTP base URL on the umbrella network.
ORCHESTRATOR_BASE_URL=http://orchestrator-api:8000
ORCHESTRATOR_API_KEY=replace-me-with-orchestrator-api-key

# Single agent per adapter instance in v1.
AGENT_REF=lifecycle-agent@0.4.0-manual

# Shared Postgres on the umbrella cluster.
ADAPTER_DB_URL=postgresql+asyncpg://devtools:devtools@postgres:5432/devhub_orchestrator_adapter
```

### Step 7: README
**File (sibling):** `README.md` · Create

Short — purpose, five translated routes (table), env-var reference, smoke-test command, link back to DevHub's `docs/orchestrator-adapter.md`.

### Step 8: Umbrella wiring
**File (cross-repo, separate PR against the umbrella root):** `../start.sh` · Modify

Add `carestechs-devhub-orchestrator-adapter` to the `PROJECTS` array, before or after the orchestrator — order doesn't matter; they health-check independently.

### Step 9: Smoke verify
**Bash:**

```bash
cd ..
./start.sh
curl -sf http://127.0.0.1:8095/health
# Expect: {"status":"ok","upstream":true}
```

## Files Affected
| File | Action | Repo |
|------|--------|------|
| `pyproject.toml` | Create | adapter |
| `Dockerfile` | Create | adapter |
| `docker-compose.prod.yml` | Create | adapter |
| `.env.production.example` | Create | adapter |
| `README.md` | Create | adapter |
| `src/adapter/{__init__,config,main}.py` | Create | adapter |
| `.gitignore` | Create | adapter |
| `../start.sh` PROJECTS entry | Modify | umbrella |

## Edge Cases & Risks
- **Orchestrator not yet running when the adapter boots.** `/health` returns 503; the umbrella's health-gate loop in `start.sh` waits up to 60s. Acceptable.
- **Container name collision.** `devhub-orchestrator-adapter` is new; no collision.
- **Python style preference.** The brief assumed FastAPI + pyproject.toml; if the operator wants `poetry` or `uv` instead, this scaffolding is small enough to swap. Confirm before writing the lockfile.

## Acceptance Verification
- [ ] `cd .. && ./start.sh` brings the adapter up alongside other umbrella services.
- [ ] `curl -sf http://127.0.0.1:8095/health` returns 200 within 30 s of boot.
- [ ] Container name `devhub-orchestrator-adapter` is on the `devtools-infra` network.
- [ ] No secrets committed.
