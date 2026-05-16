# Feature Brief: FEAT-007 — Umbrella Shared-Infra Deployment

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-007 |
| **Name** | Umbrella Shared-Infra Deployment |
| **Target Version** | v1 |
| **Status** | Not Started |
| **Priority** | Medium |
| **Requested By** | Operator (umbrella unification across DevTools projects) |
| **Date Created** | 2026-05-16 |

## 2. User Story

**As an** operator running multiple DevTools projects on one host, **I want to** bring DevHub up and down as part of the umbrella (`../start.sh` / `../stop.sh`) on the shared `devtools-infra` Docker network and the shared Postgres cluster, **so that** DevHub uses container-name DNS to reach other projects (and they reach it), shares one Postgres volume, and stops fighting other projects for host ports.

## 3. Goal

Adapt DevHub's prod deployment to the umbrella convention already in use by `carestechs-flow-engine`, `carestechs-agent-orchestrator`, and `carestechs-agent-orchestrator-ui`: one shared `postgres` container, one external `devtools-infra` network, one-command lifecycle. Solo-dev `docker-compose.yml` keeps working unchanged in shape, but its host port shifts so it can coexist with the umbrella's postgres on the same host.

Reference spec: `docs/umbrella-adaptation.md`. This FEAT is the implementation of that spec.

## 4. Feature Scope

### 4.1 Included

- `docker-compose.prod.yml` rewrite: drop the project-owned `postgres` service and `devhub-pgdata` volume; both `api` and `web` join the external `devtools-infra` network; connection string built from shared `POSTGRES_*` env vars pointing at host `postgres:5432`.
- `web` published at `127.0.0.1:${WEB_HOST_PORT:-4300}:80`.
- `api` published at `127.0.0.1:${API_HOST_PORT:-8090}:8080` (loopback only, matches `orchestrator-api`'s ops ergonomic).
- `client/nginx.conf`: upstream confirmed to use container-name DNS (`devhub-api:8080`).
- `.env.production.example` updated: shared Postgres creds (`devtools`/`devtools`), `POSTGRES_DB=devhub`, `WEB_HOST_PORT=4300`, `API_HOST_PORT=8090`, `Cors__SpaOrigin=http://127.0.0.1:4300`; remove explicit `ConnectionStrings__Postgres` (built by compose).
- `../infra/init-databases.sql` patched to add `CREATE DATABASE devhub;` (one line in the sibling infra repo).
- Documented manual one-shot for already-initialized infra volumes: `docker exec -i postgres psql -U devtools -d postgres -c 'CREATE DATABASE devhub;'`.
- `../start.sh` PROJECTS list extended to include `carestechs-dev-hub`.
- Solo-dev `docker-compose.yml` host port shifted to `127.0.0.1:5434:5432`; `.env.example` connection string updated to `Port=5434`.
- `scripts/verify-docker.sh` scoped to standalone mode only (Option A from the spec) — header comment clarifying it must not run against the umbrella prod compose.
- `README.md` and `CLAUDE.md` gain a short "Umbrella mode" pointer to `docs/umbrella-adaptation.md` and `../devtools-umbrella.md`.

### 4.2 Excluded

- Caddy / TLS / domain routing for DevHub (non-goal per `devtools-umbrella.md`).
- Migrating dev-mode flow (`dotnet run` + `ng serve` against local Postgres) to the umbrella — local dev keeps its own Postgres container.
- Multi-host or Swarm/Kubernetes deployment.
- Backups, rotation, or HA for the shared Postgres volume.
- New umbrella tooling (e.g., per-project subcommands in `start.sh`); we only extend the existing `PROJECTS` list.

## 5. Acceptance Criteria

- **AC-1:** With a clean Docker state, running `cd ../infra && docker compose up -d` followed by `cd ../carestechs-dev-hub && cp .env.production.example .env.production && docker compose -f docker-compose.prod.yml --env-file .env.production up -d --build` brings `devhub-api` and `devhub-web` to `healthy`, both attached to network `devtools-infra` alongside container `postgres`.
- **AC-2:** `curl -sf http://127.0.0.1:4300/` returns 200 and the response body contains `<title>DevHub</title>`.
- **AC-3:** `POST http://127.0.0.1:4300/api/auth/login` with the seeded operator returns a JWT; `GET /api/auth/me` with that bearer returns the seeded email.
- **AC-4:** `docker exec orchestrator-api curl -sf http://devhub-api:8080/health` succeeds — proves DevHub is reachable by sibling projects via container DNS on the shared network.
- **AC-5:** `cd .. && ./start.sh` brings up the whole umbrella (orchestrator, flow-engine, ao-ui, devhub) in one command with no port collisions; `./stop.sh` tears it down without removing the shared infra volume.
- **AC-6:** `docker-compose.prod.yml` declares no `postgres` service and no project-owned volume; the `infra` network is declared as `external: true` with `name: devtools-infra`.
- **AC-7:** Solo-dev path still works: with the umbrella down, `docker compose up -d` + `dotnet run --project src/DevHub.Api` boots against the project-local Postgres on `127.0.0.1:5434`.
- **AC-8:** Solo-dev and umbrella can coexist on the same host without a port collision on Postgres (`5434` vs `5433`) or on the SPA (`4200` ao-ui vs `4300` devhub-web).

## 6. Key Entities and Business Rules

No new domain entities. New cross-project deployment contract:

| Contract | Role | Rules |
|----------|------|-------|
| Shared Postgres cluster | One container, many DBs | DB `devhub` owned by `devtools`; created by `infra/init-databases.sql` |
| `devtools-infra` network | Cross-project DNS | External, declared in every project's prod compose as `external: true` |
| Container naming | Discovery key | `devhub-api`, `devhub-web` — peers reach DevHub by these names |
| Host port band | Loopback-only publication | UIs on `4xxx` (ao-ui `4200`, devhub-web `4300`); APIs on `8xxx` (orchestrator `8000`, devhub-api `8090`) |

## 7. API Impact

None. No endpoint, DTO, or contract change. Connection string moves from `.env` to compose-built; that is invisible to API consumers.

## 8. UI Impact

None at the application level. Only the **published host port** changes (`8080` → `4300`), and `Cors__SpaOrigin` follows it. Operator-facing URL after the change: `http://127.0.0.1:4300`.

## 9. Edge Cases

- **Existing infra volume on the operator's machine.** `init-databases.sql` only runs on first volume init; on already-initialized hosts the `devhub` database will not auto-exist. Mitigation: documented one-shot `docker exec` command in the spec and in this brief's scope.
- **Operator runs solo `docker-compose.yml` while umbrella is up.** Port shift to `5434` prevents the collision; without the shift both would fight for `5432`.
- **CORS misconfiguration.** `Cors__SpaOrigin` must match the published web URL exactly (scheme + host + port). If `WEB_HOST_PORT` is overridden, `Cors__SpaOrigin` must be overridden in lockstep — documented in `.env.production.example` comments.
- **JWT signing key rotation across the umbrella.** DevHub's JWT is independent of other projects' auth (the orchestrator has its own API key model); rotating DevHub's `Jwt__SigningKey` invalidates DevHub sessions only.
- **Seeded operator password drift.** `OperatorSeed__Password` is honored only on the first boot (when the seeded member does not exist). Changing it later in `.env` has no effect — documented behavior, not a regression.
- **`verify-docker.sh` accidentally pointed at the umbrella compose.** The script ends with `down -v`, which under the umbrella would not destroy infra Postgres (it isn't in this compose) but would still tear down devhub-api/web mid-session. Header comment plus refusing to read `docker-compose.prod.yml` enforce standalone-only use.

## 10. Constraints

- The orchestrator-as-gateway rule from the stakeholder definition is unaffected: DevHub remains the only front door humans use; lifecycle executors stay headless. This FEAT is plumbing only.
- No project-private credentials may move into the shared infra layer beyond what is already there (`POSTGRES_USER` / `POSTGRES_PASSWORD`). JWT key, operator seed, and CORS stay in DevHub's own `.env.production`.
- Streaming hot-path (SSE / chunked) must continue to pass through nginx without buffering after the upstream hostname switch. The nginx config change is hostname-only; no buffering/timeouts touched.
- Authorization-first endpoint posture is unaffected; no controller code changes.

## 11. Motivation and Priority Justification

**Motivation:** DevHub is currently a standalone deployment with its own Postgres and host port `8080`. Once the operator runs the full DevTools stack (orchestrator, flow-engine, ao-ui, DevHub), the per-project Postgres volumes and ad-hoc ports become friction: backups, restarts, and inter-project calls all assume one shared network and one shared cluster. The umbrella convention already exists and is in use by three projects; DevHub joining it removes the last special case.

**Impact if delayed:** Operators continue to coordinate ports and credentials by hand; DevHub cannot be reached by orchestrator/flow-engine via container DNS, which forces workarounds in any future feature that lets executors call back into DevHub.

**Dependencies on this feature:** None block other FEATs, but it is a prerequisite for any future flow where DevHub and a lifecycle executor need bidirectional in-network calls (e.g., executor webhooks → DevHub façade callbacks).

## 12. Traceability

| Reference | Link |
|-----------|------|
| **Persona** | Operator |
| **Stakeholder Scope Item** | Operator self-service; front-door discipline (operator stops being the routing layer) |
| **Success Metric** | Operator self-service ratio (one-command umbrella up/down) |
| **Related Work Items** | None block. Enables future executor → DevHub callback features. |
| **Spec** | `docs/umbrella-adaptation.md` |
| **Umbrella pattern** | `../devtools-umbrella.md` |
| **Reference implementations** | `../carestechs-agent-orchestrator/docker-compose.prod.yml`, `../carestechs-flow-engine/docker-compose.prod.yml`, `../carestechs-agent-orchestrator-ui/docker-compose.prod.yml` |
