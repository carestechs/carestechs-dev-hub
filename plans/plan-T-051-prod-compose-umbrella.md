# Implementation Plan: T-051 — docker-compose.prod.yml + nginx + .env.production + README/CLAUDE

## Task Reference
- **Task ID:** T-051 · **Type:** DevOps · **Workflow:** standard · **Complexity:** M
- **Rationale:** Bulk of the FEAT-007 footprint. AC-1, AC-2, AC-3, AC-4, AC-6 land here. The order matters because nginx upstream must change in lockstep with the API's container name.

## Overview
Rewrite `docker-compose.prod.yml` per the spec in `docs/umbrella-adaptation.md` §2. Update the SPA nginx upstream, the env example, the verify-docker scope note, and add a discoverability paragraph to `README.md` and `CLAUDE.md`.

## Implementation Steps

### Step 1: Rewrite `docker-compose.prod.yml`
**File:** `docker-compose.prod.yml` · Modify

Drop:
- `postgres` service (entire block)
- `volumes: { devhub-pgdata: }` (no longer owned by this compose)
- `api.depends_on.postgres`

Keep `api` + `web`. Both join `networks: [infra]` where `infra` is `external: true, name: devtools-infra`.

API service:
```yaml
api:
  image: devhub-api:latest
  container_name: devhub-api
  build: { context: . }
  environment:
    ConnectionStrings__Postgres: "Host=postgres;Port=5432;Database=${POSTGRES_DB:-devhub};Username=${POSTGRES_USER:-devtools};Password=${POSTGRES_PASSWORD:-devtools}"
    Jwt__Issuer:     "${Jwt__Issuer}"
    Jwt__Audience:   "${Jwt__Audience}"
    Jwt__SigningKey: "${Jwt__SigningKey}"
    Cors__SpaOrigin: "${Cors__SpaOrigin}"
    OperatorSeed__Email:       "${OperatorSeed__Email}"
    OperatorSeed__DisplayName: "${OperatorSeed__DisplayName}"
    OperatorSeed__Password:    "${OperatorSeed__Password}"
    ASPNETCORE_URLS: "http://+:8080"
    ASPNETCORE_ENVIRONMENT: "Production"
  healthcheck:
    test: ["CMD-SHELL", "wget -qO- http://127.0.0.1:8080/health > /dev/null 2>&1 || exit 1"]
    interval: 10s
    timeout: 3s
    retries: 12
  networks: [infra]
  ports:
    # Loopback-only host bind for ops curls; peers reach by container name.
    - "127.0.0.1:${API_HOST_PORT:-8090}:8080"
  restart: unless-stopped
```

Web service:
```yaml
web:
  image: devhub-web:latest
  container_name: devhub-web
  build: { context: ./client }
  depends_on:
    api: { condition: service_healthy }
  networks: [infra]
  ports:
    - "127.0.0.1:${WEB_HOST_PORT:-4300}:80"
  healthcheck:
    test: ["CMD-SHELL", "wget -qO- http://127.0.0.1/health > /dev/null 2>&1 || exit 1"]
    interval: 10s
    timeout: 3s
    retries: 6
  restart: unless-stopped
```

Networks block:
```yaml
networks:
  infra:
    external: true
    name: devtools-infra
```

### Step 2: Update nginx upstream
**File:** `client/nginx.conf` · Modify

Change `proxy_pass http://api:8080/api/;` to `proxy_pass http://devhub-api:8080/api/;`. Keep the SSE preservation block (`proxy_buffering off;`, `proxy_request_buffering off;`) intact — DO NOT touch any other directive.

Why explicit container name vs `api` alias: the alias works in both modes today but ties the SPA to one specific service name. Using `devhub-api` (the container name) is the umbrella convention and removes ambiguity if a sibling project also names a service `api`.

### Step 3: Rewrite `.env.production.example`
**File:** `.env.production.example` · Modify

Replace project-owned Postgres credentials with the shared cluster creds. Drop the explicit `ConnectionStrings__Postgres` (compose builds it from `POSTGRES_*`).

```dotenv
# Shared infra Postgres — must match infra/.env
POSTGRES_USER=devtools
POSTGRES_PASSWORD=devtools
POSTGRES_DB=devhub

# JWT — required, must be a 32+ byte random string
Jwt__Issuer=https://devhub.local
Jwt__Audience=devhub-spa
Jwt__SigningKey=replace-me-with-at-least-32-byte-random-string

# Seeded operator (created at first boot only; rotate immediately after)
OperatorSeed__Email=operator@devhub.local
OperatorSeed__DisplayName=Operator
OperatorSeed__Password=replace-me-and-rotate-immediately

# CORS — must match WEB_HOST_PORT exactly (scheme + host + port)
Cors__SpaOrigin=http://127.0.0.1:4300

# Host ports (loopback-only). UI on the 4xxx band, API on the 8xxx band.
WEB_HOST_PORT=4300
API_HOST_PORT=8090
```

Add an inline comment near `Cors__SpaOrigin`: "If you override `WEB_HOST_PORT`, override this in lockstep."

### Step 4: Annotate `verify-docker.sh` as standalone-only
**File:** `scripts/verify-docker.sh` · Modify

Add a banner-style comment block at the top:
```bash
# scripts/verify-docker.sh — STANDALONE prod-stack smoke test.
#
# *** DO NOT run against the umbrella ***
#   This script is hard-wired to the standalone docker-compose.prod.yml flow,
#   uses the .env.production credentials, and ends with `down -v` which would
#   tear down devhub-api/web mid-session if the umbrella stack is up.
#
# For the umbrella smoke test, use scripts/verify-umbrella.sh (T-054).
```

No script logic changes. The standalone compose was rewritten in T-051 step 1 — if an operator runs this against the new prod compose they get a "no postgres service" error and the script exits.

### Step 5: README + CLAUDE umbrella sections
**File:** `README.md` · Modify

Add near the existing "Local Development" / "Production" block:

```markdown
### Umbrella mode

Run DevHub alongside the other DevTools projects (orchestrator, flow-engine,
ao-ui) against shared infra. Requires the umbrella's `devtools-infra` network
and `postgres` container to be up first.

1. Bring up the shared infra: `cd ../infra && docker compose up -d`
2. Confirm the `devhub` database exists (one-time, on already-initialized
   volumes only): `docker exec -i postgres psql -U devtools -d postgres -c 'CREATE DATABASE devhub;'`
3. From this repo: `cp .env.production.example .env.production` (edit JWT key + operator password)
4. `docker compose -f docker-compose.prod.yml --env-file .env.production up -d --build`
5. SPA: <http://127.0.0.1:4300>

Or, from the DevTools umbrella root: `./start.sh` brings everything up
in one command. See `docs/umbrella-adaptation.md` and `../devtools-umbrella.md`.

Smoke test (umbrella mode): `./scripts/verify-umbrella.sh`.
```

**File:** `CLAUDE.md` · Modify

Add a short bullet under "Quick Reference → Common Commands" pointing at the same docs:

```markdown
### Umbrella mode (shared DevTools infra)

DevHub can run under the `../start.sh` umbrella against a shared `postgres`
container and `devtools-infra` network. SPA is at `http://127.0.0.1:4300`;
see `docs/umbrella-adaptation.md` for the deployment contract and
`../devtools-umbrella.md` for the convention.
```

## Files Affected
| File | Action |
|------|--------|
| `docker-compose.prod.yml` | Rewrite |
| `client/nginx.conf` | Modify (upstream hostname) |
| `.env.production.example` | Rewrite |
| `scripts/verify-docker.sh` | Modify (header only) |
| `README.md`, `CLAUDE.md` | Modify (umbrella section) |

## Edge Cases & Risks
- **API on `127.0.0.1:8090` is optional.** If the operator wants internal-only, strip the `ports:` block on `api:` — peers still reach via `devhub-api:8080`. Documented inline.
- **Cors__SpaOrigin lockstep.** Strictly the published web URL. Misconfiguration → SPA can hit `/api/*` but every preflight 403s. Comment in `.env.production.example` makes this loud.
- **Old `devhub-pgdata` volume.** Operators on hosts that ran the previous standalone prod compose have a `devhub-pgdata` volume sitting around. It's no longer referenced by anything; safe to leave or `docker volume rm devhub-pgdata`. Document in the README umbrella section as a one-time hygiene note.

## Acceptance Verification
- [ ] `docker compose -f docker-compose.prod.yml config` parses without error.
- [ ] `docker compose -f docker-compose.prod.yml up -d --build` succeeds when the `devtools-infra` network + `postgres` container + `devhub` database all pre-exist.
- [ ] `curl -sf http://127.0.0.1:4300/` returns SPA index.
- [ ] `curl -sf http://127.0.0.1:8090/health` returns `Healthy` (validates the optional API host bind).
- [ ] `docker exec orchestrator-api curl -sf http://devhub-api:8080/health` succeeds (AC-4 — needs orchestrator running).
