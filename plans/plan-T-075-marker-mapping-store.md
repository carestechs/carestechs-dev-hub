# Implementation Plan: T-075 — Marker↔run-id mapping store

## Task Reference
- **Task ID:** T-075 · **Type:** Backend (adapter) · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-2 + everything downstream needs to translate marker → run_id.

## Overview
SQLAlchemy 2.0 async model + Alembic migration on the shared Postgres cluster. One table (`marker_mapping`), three repository functions, a sibling-repo patch to `../infra/init-databases.sql` adding the new DB.

## Implementation Steps

### Step 1: Create the DB
**Cross-repo patch** to `../infra/init-databases.sql`:

```sql
CREATE DATABASE devhub_orchestrator_adapter;
```

Manual one-shot for already-initialized infra volumes:
```bash
docker exec -i postgres psql -U devtools -d postgres -c 'CREATE DATABASE devhub_orchestrator_adapter;'
```

### Step 2: SQLAlchemy model
**File (sibling):** `src/adapter/models.py` · Create

```python
import uuid
from datetime import datetime, timezone
from sqlalchemy import String, DateTime, Index
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column

class Base(DeclarativeBase): pass

class MarkerMapping(Base):
    __tablename__ = "marker_mapping"
    marker: Mapped[str] = mapped_column(String(64), primary_key=True)
    run_id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), unique=True, nullable=False)
    executor_id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), nullable=False)
    agent_ref: Mapped[str] = mapped_column(String(120), nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        default=lambda: datetime.now(timezone.utc),
        nullable=False,
    )
    __table_args__ = (Index("ix_marker_mapping_executor_id", "executor_id"),)
```

### Step 3: Alembic config
**File (sibling):** `alembic.ini` · Create (standard template).
**File (sibling):** `alembic/env.py` · Create — async-capable, reads DB URL from `ADAPTER_DB_URL`.
**File (sibling):** `alembic/versions/<ts>_initial.py` · Create

`Up()`: `op.create_table("marker_mapping", ...)` matching the model. `Down()`: `op.drop_table("marker_mapping")`.

### Step 4: Repository
**File (sibling):** `src/adapter/store.py` · Create

```python
import uuid
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine
from adapter.config import Settings
from adapter.models import MarkerMapping

_settings = Settings()
_engine = create_async_engine(_settings.adapter_db_url)
_session = async_sessionmaker(_engine, expire_on_commit=False)

async def store(marker: str, run_id: uuid.UUID, executor_id: uuid.UUID, agent_ref: str) -> None:
    async with _session() as s:
        s.add(MarkerMapping(marker=marker, run_id=run_id, executor_id=executor_id, agent_ref=agent_ref))
        await s.commit()

async def lookup_run_id(marker: str) -> uuid.UUID | None:
    async with _session() as s:
        result = await s.scalar(select(MarkerMapping.run_id).where(MarkerMapping.marker == marker))
        return result

async def lookup_marker(run_id: uuid.UUID) -> str | None:
    async with _session() as s:
        result = await s.scalar(select(MarkerMapping.marker).where(MarkerMapping.run_id == run_id))
        return result
```

### Step 5: Health-check DB liveness
**File (sibling):** `src/adapter/main.py` · Modify

Extend `/health`: in addition to the upstream check, run a `SELECT 1` against the adapter DB. Return 503 if either is unreachable.

### Step 6: Alembic auto-apply on boot
**File (sibling):** `src/adapter/main.py` · Modify

Add a startup event:

```python
@app.on_event("startup")
async def run_migrations():
    from alembic.config import Config
    from alembic import command
    cfg = Config("alembic.ini")
    cfg.set_main_option("sqlalchemy.url", settings.adapter_db_url)
    command.upgrade(cfg, "head")
```

(Alternatively, run the migration as a one-shot init container in compose. Either is fine for v1.)

### Step 7: Smoke
**Bash (after T-074 + this task land):**

```bash
cd ../carestechs-devhub-orchestrator-adapter
docker compose -f docker-compose.prod.yml up -d --build
docker exec postgres psql -U devtools -d devhub_orchestrator_adapter -c '\d marker_mapping'
```

Expect the table to be present.

## Files Affected
| File | Action | Repo |
|------|--------|------|
| `src/adapter/models.py` | Create | adapter |
| `src/adapter/store.py` | Create | adapter |
| `src/adapter/main.py` | Modify | adapter |
| `alembic.ini` | Create | adapter |
| `alembic/env.py` | Create | adapter |
| `alembic/versions/<ts>_initial.py` | Create | adapter |
| `../infra/init-databases.sql` | Modify | infra (separate PR) |

## Edge Cases & Risks
- **Alembic migration on already-running adapter.** Idempotent via `command.upgrade("head")`; no-op when already at head.
- **Cross-service DB ownership.** The orchestrator's tables are in its own DB; the adapter's `marker_mapping` is in its own DB. No shared schema, no cross-service writes. Clean.
- **`X-DevHub-Executor-Id` header.** DevHub's `ExecutorHttpClient` sets `X-DevHub-Correlation` but I don't see an executor-id header on outbound calls. T-077 will resolve `executor_id` from the inbound request (e.g. from a header DevHub adds, or fall back to a single configured value when missing).

## Acceptance Verification
- [ ] Table exists after first boot.
- [ ] `store / lookup_run_id / lookup_marker` work via a quick async test.
- [ ] `/health` returns 503 when the DB is unreachable.
