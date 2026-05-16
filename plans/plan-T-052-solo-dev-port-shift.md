# Implementation Plan: T-052 — solo-dev compose port shift to 5434

## Task Reference
- **Task ID:** T-052 · **Type:** DevOps · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-7 + AC-8. The umbrella publishes shared Postgres on `127.0.0.1:5433`; the solo dev compose currently grabs `127.0.0.1:5432`. Both can coexist on the same host as long as they use different host ports.

## Overview
Two-line change in `docker-compose.yml` (solo dev) and one line in `.env.example`. Verify that `dotnet run` against the shifted port still boots.

## Implementation Steps

### Step 1: Shift Postgres host port
**File:** `docker-compose.yml` · Modify

```yaml
ports:
  - "127.0.0.1:5434:5432"
```

Note: bind to loopback only (matches the umbrella's stance). The previous `5432:5432` was on the wildcard interface.

### Step 2: Update `.env.example` connection string
**File:** `.env.example` · Modify

```dotenv
ConnectionStrings__Postgres=Host=localhost;Port=5434;Database=devhub;Username=devhub;Password=change-me
```

Everything else in `.env.example` stays — the solo flow keeps its own Postgres user (`devhub`), its own DB (`devhub`), its own password (`change-me` placeholder). It's a self-contained dev flow.

### Step 3: Manual smoke
1. `docker compose up -d` → `devhub-postgres-dev` healthy on `127.0.0.1:5434`.
2. `psql -h localhost -p 5434 -U devhub devhub` succeeds.
3. `cp .env.example .env` (if not already) and `dotnet run --project src/DevHub.Api` → API starts on its usual host port; `GET /health` returns 200.
4. `dotnet test` → 132/132 backend tests still pass (Testcontainers spins fresh containers on random ports — unaffected).

## Files Affected
| File | Action |
|------|--------|
| `docker-compose.yml` | Modify (`ports:`) |
| `.env.example` | Modify (`ConnectionStrings__Postgres`) |

## Edge Cases & Risks
- **Operator has existing `.env` (not example).** Their `.env` still uses `Port=5432` because the rewrite only touches `.env.example`. The README should call out: "after pulling this change, update your `.env` `Port` from 5432 to 5434." Add a note in the README "Local Development" section (this PR can include it).
- **TablePlus / DBeaver / external clients.** Anyone with a saved connection profile pointing at `localhost:5432` must update it. One-time tax.
- **Why 5434 specifically.** 5432 = upstream default, 5433 = umbrella's shared cluster, 5434 = next free slot. Symmetric and self-documenting.

## Acceptance Verification
- [ ] `docker compose up -d` brings Postgres up on `127.0.0.1:5434`.
- [ ] `dotnet run` against the shifted connection string boots.
- [ ] `dotnet test` is green.
- [ ] If the umbrella's `infra/` Postgres is also up on `127.0.0.1:5433`, both containers coexist with no port collision.
