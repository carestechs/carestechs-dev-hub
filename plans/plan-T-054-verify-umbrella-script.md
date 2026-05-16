# Implementation Plan: T-054 — scripts/verify-umbrella.sh

## Task Reference
- **Task ID:** T-054 · **Type:** DevOps · **Workflow:** standard · **Complexity:** S
- **Rationale:** The FEAT-007 AC checklist (AC-1..AC-4) is operator-runnable today by copy-pasting from the spec. Folding it into a script means infra refactors get a one-command regression check and the README has something concrete to point at.

## Overview
A single bash script at `scripts/verify-umbrella.sh` that automates the runnable subset of FEAT-007's acceptance criteria against an already-up umbrella. **Does not** tear anything down at the end — the umbrella is shared infrastructure.

## Implementation Steps

### Step 1: Script shell + preflight
**File:** `scripts/verify-umbrella.sh` · Create

```bash
#!/usr/bin/env bash
#
# scripts/verify-umbrella.sh — DevHub umbrella-mode smoke test.
#
# Verifies that DevHub builds + boots + serves under the shared
# devtools-infra network alongside the umbrella's postgres container.
# Exercises FEAT-007 acceptance criteria AC-1..AC-4.
#
# *** This script does NOT tear down the umbrella stack at the end. ***
#   The umbrella is shared infrastructure; teardown is the operator's call
#   (./stop.sh from the umbrella root, or `docker compose -f docker-compose.prod.yml down`
#   for just DevHub).
#
# For the standalone prod-stack smoke (with its own postgres + down -v at
# the end), use scripts/verify-docker.sh instead.

set -euo pipefail

cd "$(dirname "$0")/.."

COMPOSE_FILE="docker-compose.prod.yml"
ENV_FILE=".env.production"
WEB_HOST_PORT="${WEB_HOST_PORT:-4300}"
API_HOST_PORT="${API_HOST_PORT:-8090}"

ok()   { printf "\033[32m✓\033[0m %s\n" "$*"; }
warn() { printf "\033[33m!\033[0m %s\n" "$*"; }
fail() { printf "\033[31m✗\033[0m %s\n" "$*"; exit 1; }
```

### Step 2: AC-1 preflight — network + database exist
```bash
echo "==> AC-1 preflight: devtools-infra network + devhub database"

docker network inspect devtools-infra >/dev/null 2>&1 \
  || fail "network 'devtools-infra' not found — run 'cd ../infra && docker compose up -d' first"
ok "network devtools-infra exists"

docker exec postgres psql -U devtools -lqt 2>/dev/null \
  | cut -d'|' -f1 | tr -d ' ' | grep -qx devhub \
  || fail "database 'devhub' not found on the shared postgres — run: docker exec -i postgres psql -U devtools -d postgres -c 'CREATE DATABASE devhub;'"
ok "database 'devhub' exists on shared postgres"
```

### Step 3: Build + start
```bash
echo "==> AC-1: build + bring up devhub-api + devhub-web"

[[ -f "$ENV_FILE" ]] || { warn ".env.production not found, copying .env.production.example"; cp .env.production.example "$ENV_FILE"; }

# shellcheck disable=SC1090
set -a; source "$ENV_FILE"; set +a

docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" up -d --build

# Poll healthcheck for up to 60s.
deadline=$(( $(date +%s) + 60 ))
while (( $(date +%s) < deadline )); do
  api_state=$(docker inspect --format='{{.State.Health.Status}}' devhub-api 2>/dev/null || echo missing)
  web_state=$(docker inspect --format='{{.State.Health.Status}}' devhub-web 2>/dev/null || echo missing)
  if [[ "$api_state" == "healthy" && "$web_state" == "healthy" ]]; then
    break
  fi
  sleep 2
done
[[ "$api_state" == "healthy" ]] || fail "devhub-api not healthy (state=$api_state)"
[[ "$web_state" == "healthy" ]] || fail "devhub-web not healthy (state=$web_state)"
ok "devhub-api + devhub-web healthy"
```

### Step 4: AC-2 — SPA index served
```bash
echo "==> AC-2: SPA index served on :$WEB_HOST_PORT"

body=$(curl -sf "http://127.0.0.1:${WEB_HOST_PORT}/") || fail "GET / failed"
echo "$body" | grep -q '<title>DevHub</title>' || fail "SPA <title>DevHub</title> not found in response"
ok "SPA index served"
```

### Step 5: AC-3 — login + /me round-trip
```bash
echo "==> AC-3: operator login + /me round-trip"

login_resp=$(curl -sf -X POST "http://127.0.0.1:${WEB_HOST_PORT}/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"${OperatorSeed__Email}\",\"password\":\"${OperatorSeed__Password}\"}") \
  || fail "POST /api/auth/login failed"

token=$(echo "$login_resp" | grep -oP '"accessToken"\s*:\s*"\K[^"]+' || true)
[[ -n "$token" ]] || fail "no accessToken in login response"

me_resp=$(curl -sf "http://127.0.0.1:${WEB_HOST_PORT}/api/auth/me" \
  -H "Authorization: Bearer $token") || fail "GET /api/auth/me failed"

echo "$me_resp" | grep -q "${OperatorSeed__Email}" \
  || fail "/me response did not echo seeded operator email"
ok "login + /me round-trip"
```

### Step 6: AC-4 — cross-project DNS (optional)
```bash
echo "==> AC-4: cross-project DNS from orchestrator-api"

if docker ps --format '{{.Names}}' | grep -qx orchestrator-api; then
  docker exec orchestrator-api curl -sf http://devhub-api:8080/health >/dev/null \
    || fail "orchestrator-api could not reach devhub-api:8080/health"
  ok "orchestrator-api reaches devhub-api:8080/health"
else
  warn "orchestrator-api not running — skipping cross-project DNS check (AC-4)"
fi
```

### Step 7: Summary footer
```bash
echo
ok "umbrella smoke checks passed"
echo "    SPA:    http://127.0.0.1:${WEB_HOST_PORT}/"
echo "    API:    http://127.0.0.1:${API_HOST_PORT}/health"
echo
echo "Leaving the stack up. Tear down with:"
echo "    docker compose -f $COMPOSE_FILE down"
echo "  or, for the full umbrella, from the umbrella root:"
echo "    ./stop.sh"
```

### Step 8: chmod + README
1. `chmod +x scripts/verify-umbrella.sh`.
2. Add a line to the README umbrella section (added in T-051): "Run `./scripts/verify-umbrella.sh` to smoke-test the umbrella deployment without tearing it down."

## Files Affected
| File | Action |
|------|--------|
| `scripts/verify-umbrella.sh` | Create + chmod +x |
| `README.md` | Modify (link the script) |

## Edge Cases & Risks
- **`jq` not installed.** The `grep -oP '"accessToken":\s*"\K[^"]+'` fallback works on any host with grep-PCRE. If `jq` is preferred and present, swap it in — small, cosmetic.
- **Polling timeout (60s).** A cold cache + slow build can exceed 60s for `up -d --build`. The build runs synchronously *before* the polling loop, so it's the healthcheck wait that's bounded. 60s is generous for a healthy run; tune if a CI host hits the ceiling.
- **`OperatorSeed__Password` rotation.** After first boot the seed value is irrelevant (the row exists, the env var is honored only on first create). If the operator rotated the password via DB and the env var still holds the placeholder, login fails. Documented in the FEAT-007 brief's "edge cases."
- **Idempotency.** The script is safe to re-run. `up -d --build` rebuilds + restarts containers; healthcheck polling re-validates. The script doesn't reset the DB or seed data.

## Acceptance Verification
- [ ] `scripts/verify-umbrella.sh` exists and is executable.
- [ ] Running it from a clean repo with the umbrella up and `devhub` DB present exits 0 and prints a green summary.
- [ ] Running it with the `devtools-infra` network missing fails fast with a clear remediation line.
- [ ] Running it with `orchestrator-api` not up skips AC-4 gracefully (yellow warn, exits 0).
