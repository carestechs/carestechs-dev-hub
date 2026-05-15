# Implementation Plan: T-019 — docker-compose.prod.yml and verify-docker.sh

## Task Reference
- **Task ID:** T-019
- **Type:** DevOps
- **Workflow:** standard
- **Complexity:** M
- **Rationale:** FEAT-001 AC-6. Profile rule: prod compose on a shared `infra` network with healthchecks gating startup.

## Overview
Production-flavored compose describing the API + frontend services on a shared `infra` external network, with healthchecks and `depends_on: condition: service_healthy`. A `scripts/verify-docker.sh` smoke test brings the stack up, polls `/health`, hits the SPA root, and tears everything down on exit.

## Implementation Steps

### Step 1: .env.production.example
**File:** `.env.production.example`
**Action:** Create
```
# Postgres (on the infra network — not in this compose file)
ConnectionStrings__Postgres=Host=infra-postgres;Port=5432;Database=devhub;Username=devhub;Password=<change-me>

# JWT
Jwt__Issuer=https://devhub.example.com
Jwt__Audience=devhub-spa
Jwt__SigningKey=<replace-with-strong-32+ byte secret>

# Seed (used only on first boot when DB is empty)
OperatorSeed__Email=operator@devhub.example.com
OperatorSeed__DisplayName=Operator
OperatorSeed__Password=<change-me>

# CORS — production SPA origin
Cors__SpaOrigin=https://devhub.example.com

# Public-facing ports
WEB_HOST_PORT=80
```

### Step 2: docker-compose.prod.yml
**File:** `docker-compose.prod.yml`
**Action:** Create
```yaml
services:
  api:
    image: devhub-api:latest
    build:
      context: .
    container_name: devhub-api
    environment:
      ConnectionStrings__Postgres: ${ConnectionStrings__Postgres}
      Jwt__Issuer:     ${Jwt__Issuer}
      Jwt__Audience:   ${Jwt__Audience}
      Jwt__SigningKey: ${Jwt__SigningKey}
      Cors__SpaOrigin: ${Cors__SpaOrigin}
      OperatorSeed__Email:        ${OperatorSeed__Email}
      OperatorSeed__DisplayName:  ${OperatorSeed__DisplayName}
      OperatorSeed__Password:     ${OperatorSeed__Password}
      ASPNETCORE_ENVIRONMENT: Production
    healthcheck:
      test: ["CMD-SHELL", "wget -qO- http://127.0.0.1:8080/health > /dev/null || exit 1"]
      interval: 10s
      timeout: 3s
      retries: 12
    networks:
      - infra
    restart: unless-stopped

  web:
    image: devhub-web:latest
    build:
      context: ./client
    container_name: devhub-web
    depends_on:
      api:
        condition: service_healthy
    ports:
      - "${WEB_HOST_PORT:-80}:80"
    healthcheck:
      test: ["CMD-SHELL", "wget -qO- http://127.0.0.1/ > /dev/null || exit 1"]
      interval: 10s
      timeout: 3s
      retries: 6
    networks:
      - infra
    restart: unless-stopped

networks:
  infra:
    external: true
```

### Step 3: verify-docker.sh
**File:** `scripts/verify-docker.sh`
**Action:** Create
```bash
#!/usr/bin/env bash
set -euo pipefail

COMPOSE="docker compose -f docker-compose.prod.yml"
PORT="${WEB_HOST_PORT:-80}"

cleanup() { $COMPOSE down -v --remove-orphans >/dev/null 2>&1 || true; }
trap cleanup EXIT

echo "==> ensure 'infra' network exists"
docker network inspect infra >/dev/null 2>&1 || docker network create infra >/dev/null

echo "==> build + up"
$COMPOSE build
$COMPOSE up -d

echo "==> wait for api /health (via internal docker exec, not host port)"
deadline=$(( $(date +%s) + 120 ))
until docker exec devhub-api wget -qO- http://127.0.0.1:8080/health > /dev/null 2>&1; do
  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "api /health did not become healthy in 120s" >&2
    $COMPOSE logs api >&2 || true
    exit 1
  fi
  sleep 2
done
echo "==> api healthy"

echo "==> hit SPA root on host port :${PORT}"
deadline=$(( $(date +%s) + 60 ))
until curl -sf "http://127.0.0.1:${PORT}/" > /dev/null; do
  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "SPA did not respond on :${PORT} in 60s" >&2
    $COMPOSE logs web >&2 || true
    exit 1
  fi
  sleep 2
done
echo "==> SPA responding"

echo "==> verify api proxied through web"
curl -sf "http://127.0.0.1:${PORT}/api/health" | tee /dev/stderr | grep -q '"status":"ok"' || {
  echo "proxied /api/health did not return ok" >&2
  exit 1
}

echo "==> all checks passed"
```
Make executable: `chmod +x scripts/verify-docker.sh`.

### Step 4: README update
**File:** `README.md`
**Action:** Modify
Add a "Production smoke test" section:
```
1. cp .env.production.example .env.production && edit
2. docker network create infra (if missing)
3. infra-postgres must already be on the `infra` network
4. WEB_HOST_PORT=8081 scripts/verify-docker.sh
```

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `.env.production.example` | Create | Production env template |
| `docker-compose.prod.yml` | Create | Prod compose (api + web on `infra` network) |
| `scripts/verify-docker.sh` | Create | Smoke test (executable) |
| `README.md` | Modify | Production-smoke section |

## Edge Cases & Risks
- **infra network missing on first run** — script creates it before `compose up`; the user only needs `infra-postgres` to already be reachable on it (real Postgres, not the dev compose's container).
- **Port collision on host** — `WEB_HOST_PORT` defaults to 80; CI should override to a free port.
- **`trap cleanup EXIT` should fire even on failure** — `set -e` triggers EXIT; `cleanup` tears the stack down. Verified manually.
- **API takes long to seed on a fresh DB** — bumping `retries` to 12 (≈ 2 min total) keeps the timeout generous.

## Acceptance Verification
- [ ] `docker compose -f docker-compose.prod.yml build` succeeds.
- [ ] `scripts/verify-docker.sh` exits 0 on a clean machine with a reachable Postgres on the `infra` network.
- [ ] The script tears the stack down whether it succeeds or fails (`docker ps` empty after).
- [ ] No image bakes in `.env*` files (`docker run --rm devhub-api find / -name ".env*"` returns empty paths outside `/proc`).
