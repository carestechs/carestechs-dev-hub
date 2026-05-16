# FEAT-007 Task Breakdown — Umbrella Shared-Infra Deployment

> Generated from `docs/work-items/FEAT-007-umbrella-shared-infra.md` (and its source spec `docs/umbrella-adaptation.md`) using `.ai-framework/prompts/feature-tasks.md`. 4 tasks across DevOps + Documentation. No application code touched.

## Scope choices locked in before generation

- **Two repos, one feature.** The DevHub-side changes (T-051 + T-052 + T-054) live in this repo. The sibling-repo changes (T-053) land via separate PRs against `../infra` and the umbrella root. This task file documents both halves; T-053's deliverable is the patch text + the runbook, not a commit in this repo.
- **No service code touched.** Connection string moves from `.env` to compose-built; nginx upstream switches hostname only; no controller, service, or test changes.
- **Solo-dev path stays working.** The `docker-compose.yml` (solo) keeps its own Postgres; we only shift its host port from 5432 to 5434 so it can coexist with the umbrella's `127.0.0.1:5433`.
- **`verify-docker.sh` stays standalone-only.** Per the spec's Option A: header comment makes it explicit, no parameterization for umbrella mode. T-054 adds a separate `verify-umbrella.sh` for the umbrella smoke path so neither script knows about the other's compose.
- **Optional API host publication.** Per the spec's recommendation: publish `devhub-api` on `127.0.0.1:8090` (loopback only) to match `orchestrator-api`'s ops ergonomic. Documented; trivially removable if the operator prefers internal-only.
- **Ports locked:** UI = `4300`, API = `8090`. Both loopback-only.

---

## DevOps

### T-051: docker-compose.prod.yml + nginx + .env.production + README/CLAUDE umbrella section

**Type:** DevOps · **Workflow:** standard · **Complexity:** M · **Dependencies:** None

**Description:**
Rewrite `docker-compose.prod.yml` to drop the project-owned Postgres + volume, join the external `devtools-infra` network, and bind both services to loopback host ports (`web:4300`, `api:8090`). Switch `client/nginx.conf` to `proxy_pass http://devhub-api:8080/api/` (container DNS). Update `.env.production.example` to use the shared Postgres creds (`devtools`/`devtools`) and the new ports. Add a header comment to `scripts/verify-docker.sh` declaring it standalone-only. Add a short "Umbrella mode" section to `README.md` and `CLAUDE.md`.

**Rationale:**
The bulk of the FEAT-007 footprint in this repo. AC-1, AC-2, AC-3, AC-4, AC-6 land here. The order matters: nginx upstream must change in lockstep with the API's container name so the SPA can still reach the API after the host-port move.

**Acceptance Criteria:**
- [ ] `docker-compose.prod.yml` declares no `postgres` service and no `devhub-pgdata` volume; both `api` and `web` join `networks: [infra]` where `infra` is `external: true, name: devtools-infra`.
- [ ] API connection string is built from shared `POSTGRES_*` env vars and resolves to `postgres:5432` (container DNS) — no host loopback.
- [ ] Web is published on `127.0.0.1:${WEB_HOST_PORT:-4300}:80`. API is published on `127.0.0.1:${API_HOST_PORT:-8090}:8080`.
- [ ] `client/nginx.conf` `proxy_pass` uses `http://devhub-api:8080/api/` (explicit container name, not the `api:` alias).
- [ ] `.env.production.example` uses `POSTGRES_USER=devtools`, `POSTGRES_PASSWORD=devtools`, `POSTGRES_DB=devhub`, `Cors__SpaOrigin=http://127.0.0.1:4300`, `WEB_HOST_PORT=4300`, `API_HOST_PORT=8090`; drops the explicit `ConnectionStrings__Postgres` (compose builds it).
- [ ] `scripts/verify-docker.sh` gets a top-of-file warning: "standalone-only; do NOT run against the umbrella prod compose."
- [ ] `README.md` and `CLAUDE.md` gain a short pointer to `docs/umbrella-adaptation.md` + `../devtools-umbrella.md`.

**Files to Modify/Create:**
- Modify: `docker-compose.prod.yml` (rewrite)
- Modify: `client/nginx.conf` (`proxy_pass` hostname)
- Modify: `.env.production.example`
- Modify: `scripts/verify-docker.sh` (header comment only)
- Modify: `README.md`, `CLAUDE.md`

**Technical Notes:**
The `api:` proxy_pass alias works under the dev `docker-compose.yml` (both services on a compose-generated network) AND under the umbrella prod compose (compose maps the service name `api` to the container `devhub-api` on the `infra` network). Switching to the explicit `devhub-api` hostname removes the ambiguity for anyone joining a third project that also names a service `api`.

The `infra` short-name in compose maps to the external network name `devtools-infra` — both must be declared per the umbrella convention.

---

### T-052: solo-dev compose port shift to 5434 + .env.example

**Type:** DevOps · **Workflow:** standard · **Complexity:** S · **Dependencies:** None (parallel-safe with T-051)

**Description:**
Shift the solo-dev `docker-compose.yml` Postgres host port from `127.0.0.1:5432:5432` to `127.0.0.1:5434:5432`. Update `.env.example` connection string to `Port=5434`. This lets the solo flow and the umbrella coexist on the same host (umbrella Postgres is on `127.0.0.1:5433`).

**Rationale:**
AC-7, AC-8. Without the shift, an operator who forgets to stop one compose before starting the other gets a confusing "port in use" error.

**Acceptance Criteria:**
- [ ] `docker-compose.yml` (solo) maps `127.0.0.1:5434:5432`.
- [ ] `.env.example`'s `ConnectionStrings__Postgres` uses `Port=5434`.
- [ ] `docker compose up -d` followed by `dotnet run --project src/DevHub.Api` boots successfully and `GET /health` returns 200 on `http://localhost:5000` (or whatever the API host is).
- [ ] Existing `dotnet test` suite still passes (Testcontainers uses dynamic ports — unaffected).

**Files to Modify/Create:**
- Modify: `docker-compose.yml`
- Modify: `.env.example`

**Technical Notes:**
Testcontainers spins up Postgres on a random container-mapped port, so the 5432-vs-5434 shift is invisible to tests. Only solo-dev users hitting Postgres directly via psql / TablePlus need to know.

---

### T-053: Sibling-repo patches (../infra/init-databases.sql + ../start.sh PROJECTS)

**Type:** DevOps · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-051 (compose change must merge first so the sibling repos point at something real)

**Description:**
Land two patches in sibling repos:
1. `../infra/init-databases.sql` — append `CREATE DATABASE devhub;`.
2. `../start.sh` — add `carestechs-dev-hub` to the `PROJECTS=(...)` array.

This task's deliverable in *this* repo is the runbook + the patch text in `docs/umbrella-adaptation.md` (already present) and a follow-up checklist. The actual commits land in `../infra` and the umbrella root.

**Rationale:**
AC-1, AC-5. The `init-databases.sql` patch is what materializes the `devhub` database on a clean infra volume; the `start.sh` patch is what makes `./start.sh` bring DevHub up alongside the other projects.

**Acceptance Criteria:**
- [ ] Branch + PR opened in `../infra` adding the one-line `CREATE DATABASE devhub;` to `init-databases.sql`. PR description references this task and the runbook for already-initialized volumes.
- [ ] Branch + PR opened in the umbrella root repo adding `carestechs-dev-hub` to the `PROJECTS` array in `start.sh`. PR description references the umbrella convention doc.
- [ ] DevHub repo's `docs/umbrella-adaptation.md` already documents the manual one-shot for hosts where the infra volume pre-dates the patch (`docker exec -i postgres psql -U devtools -d postgres -c 'CREATE DATABASE devhub;'`). No change needed unless wording can be tightened.

**Files to Modify/Create:**
- (Sibling-repo) `../infra/init-databases.sql`
- (Sibling-repo) `../start.sh`
- Verify: `docs/umbrella-adaptation.md` (existing — confirm one-shot is documented)

**Technical Notes:**
`init-databases.sql` runs only on first volume init. The DevHub repo can't enforce the sibling-repo merges; the runbook in `docs/umbrella-adaptation.md` is the source of truth. Document inline that this task's "Done" condition is "PR open in both sibling repos" + linking back to this work item.

If the operator runs the umbrella without merging the sibling PRs first, `devhub-api` will fail health on a missing database. That's a loud, immediate failure — no silent corruption.

---

### T-054: scripts/verify-umbrella.sh — automated smoke test for AC-1..AC-4

**Type:** DevOps · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-051

**Description:**
Add `scripts/verify-umbrella.sh` that automates the runnable subset of the FEAT-007 acceptance criteria (AC-1..AC-4) end-to-end:
1. Assert the `devtools-infra` network exists.
2. Assert `docker exec postgres psql -U devtools -lqt | grep devhub` succeeds.
3. `docker compose -f docker-compose.prod.yml --env-file .env.production up -d --build`.
4. Poll `http://127.0.0.1:4300/health` until healthy (or timeout).
5. `curl http://127.0.0.1:4300/api/auth/login` with the seeded operator → JWT.
6. `curl http://127.0.0.1:4300/api/auth/me` → echo email.
7. (Optional, if `orchestrator-api` is up) `docker exec orchestrator-api curl -sf http://devhub-api:8080/health`.
8. **Does NOT** tear down at the end — the umbrella is shared infrastructure; teardown is the operator's call.

**Rationale:**
The AC checklist is operator-runnable today via copy-paste from `docs/umbrella-adaptation.md`. Folding it into a script means future infra refactors get a one-command regression check.

**Acceptance Criteria:**
- [ ] `scripts/verify-umbrella.sh` exists; `set -euo pipefail`; runs from any CWD.
- [ ] Script header comment makes the no-teardown policy explicit (contrast with `verify-docker.sh` which does `down -v`).
- [ ] Script exits 0 on success, non-zero on any failed AC check, with a clear error line identifying which step failed.
- [ ] Script skips the cross-project check (step 7) gracefully when `orchestrator-api` isn't running — prints a note, doesn't fail.
- [ ] `README.md` umbrella section (added in T-051) references this script.

**Files to Modify/Create:**
- Create: `scripts/verify-umbrella.sh` (executable: `chmod +x`)
- Modify: `README.md` (add the script to the umbrella section)

**Technical Notes:**
Use `curl -sf` for all HTTP probes; `jq` for JWT extraction if it's available, else fall back to `grep -oP '"accessToken":\s*"\K[^"]+'`. The script's polling loop should cap at 60s — slow CI hosts can take that long for `dotnet ef database update` to finish on a fresh DB.

---

## Summary

| Group | Count | Tasks |
|-------|-------|-------|
| DevOps | 4 | T-051, T-052, T-053, T-054 |
| **Total** | **4** | |

**Complexity:** S=3, M=1.

**Critical path:** T-051 → T-053 → T-054. T-052 parallel-safe (only touches solo-dev compose).

**Risk register:**
- **Sibling-repo merge timing.** T-053 lands changes in `../infra` and the umbrella root. If those PRs land *before* T-051, the umbrella's `./start.sh` will try to bring up a `carestechs-dev-hub` whose `docker-compose.prod.yml` still expects an internal Postgres → fails loudly. Recommended merge order: T-051 → T-053 sibling PRs → optionally T-054.
- **Existing infra volume.** If the operator's `infra/` Postgres volume already exists, `init-databases.sql` does NOT re-run. The runbook one-shot (`docker exec ... CREATE DATABASE devhub;`) is mandatory in that case; failure mode is a loud `database "devhub" does not exist` on the API's first migration attempt.
- **Port collisions if `WEB_HOST_PORT`/`API_HOST_PORT` are overridden.** The CORS allowlist (`Cors__SpaOrigin`) must be overridden in lockstep with `WEB_HOST_PORT`. Documented in `.env.production.example` comments (T-051).
- **`verify-docker.sh` accidentally run against the prod compose.** The standalone-only header comment is best-effort. A stricter option: have the script grep its own `COMPOSE_FILE` value and refuse if it's `docker-compose.prod.yml`. v1 ships the header; the refusal check is a polish if it ever triggers.

## Post-Generation Checklist

- [x] All FEAT-007 ACs map to specific tasks (AC-1..AC-4 ↔ T-051 + T-054, AC-5 ↔ T-053, AC-6 ↔ T-051, AC-7/AC-8 ↔ T-052).
- [x] Repo-boundary work is called out explicitly (T-053 lives in sibling repos).
- [x] No service code touched — pure infra + docs.
- [x] Solo-dev path preserved (T-052 keeps it working alongside the umbrella).
- [x] Dependency graph is acyclic.
