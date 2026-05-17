#!/usr/bin/env bash
#
# scripts/verify-umbrella.sh — DevHub umbrella-mode smoke test.
#
# Verifies that DevHub builds + boots + serves under the shared
# devtools-infra network alongside the umbrella's postgres container.
# Exercises FEAT-007 acceptance criteria AC-1..AC-4.
#
# *** This script does NOT tear down the umbrella stack at the end. ***
#   The umbrella is shared infrastructure; teardown is the operator's call:
#     - Stop just DevHub:   docker compose -f docker-compose.prod.yml down
#     - Stop the umbrella:  ../stop.sh (from the umbrella root)
#
# For the legacy standalone prod-stack smoke (with its own postgres + a
# `down -v` at the end), use scripts/verify-docker.sh — not applicable
# to the current docker-compose.prod.yml.

set -euo pipefail

cd "$(dirname "$0")/.."

COMPOSE_FILE="docker-compose.prod.yml"
ENV_FILE=".env.production"

ok()   { printf "\033[32m✓\033[0m %s\n" "$*"; }
warn() { printf "\033[33m!\033[0m %s\n" "$*"; }
fail() { printf "\033[31m✗\033[0m %s\n" "$*"; exit 1; }
info() { printf "\033[1;34m==>\033[0m %s\n" "$*"; }

# -----------------------------------------------------------------------------
# AC-1 preflight: shared infra is up and the devhub database exists.
# -----------------------------------------------------------------------------
info "AC-1 preflight: devtools-infra network + devhub database"

docker network inspect devtools-infra >/dev/null 2>&1 \
  || fail "network 'devtools-infra' not found — run: cd ../infra && docker compose up -d"
ok "network devtools-infra exists"

docker ps --format '{{.Names}}' | grep -qx postgres \
  || fail "container 'postgres' not running — run: cd ../infra && docker compose up -d"
ok "container postgres running"

docker exec postgres psql -U devtools -lqt 2>/dev/null \
  | cut -d'|' -f1 | tr -d ' ' | grep -qx devhub \
  || fail "database 'devhub' not found on the shared postgres — run: docker exec -i postgres psql -U devtools -d postgres -c 'CREATE DATABASE devhub;'"
ok "database 'devhub' exists on shared postgres"

# -----------------------------------------------------------------------------
# AC-1 main: build + bring up devhub-api + devhub-web, wait for healthy.
# -----------------------------------------------------------------------------
info "AC-1: build + bring up devhub-api + devhub-web"

if [[ ! -f "$ENV_FILE" ]]; then
  warn "$ENV_FILE not found, copying .env.production.example"
  cp .env.production.example "$ENV_FILE"
fi

# shellcheck disable=SC1090
set -a; source "$ENV_FILE"; set +a

WEB_HOST_PORT="${WEB_HOST_PORT:-4300}"
API_HOST_PORT="${API_HOST_PORT:-8090}"

docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" up -d --build

# Poll healthcheck for up to 60s.
deadline=$(( $(date +%s) + 60 ))
api_state=missing
web_state=missing
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

# -----------------------------------------------------------------------------
# AC-2: SPA index served on the published host port.
# -----------------------------------------------------------------------------
info "AC-2: SPA index served on :$WEB_HOST_PORT"

body=$(curl -sf "http://127.0.0.1:${WEB_HOST_PORT}/") \
  || fail "GET http://127.0.0.1:${WEB_HOST_PORT}/ failed"
echo "$body" | grep -q '<title>DevHub</title>' \
  || fail "SPA <title>DevHub</title> not found in response"
ok "SPA index served"

# -----------------------------------------------------------------------------
# AC-3: operator login → JWT → /api/auth/me round-trip.
# -----------------------------------------------------------------------------
info "AC-3: operator login + /api/auth/me round-trip"

login_resp=$(curl -sf -X POST "http://127.0.0.1:${WEB_HOST_PORT}/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"${OperatorSeed__Email}\",\"password\":\"${OperatorSeed__Password}\"}") \
  || fail "POST /api/auth/login failed (operator: ${OperatorSeed__Email})"

token=$(echo "$login_resp" | grep -oP '"accessToken"\s*:\s*"\K[^"]+' || true)
[[ -n "$token" ]] || fail "no accessToken in login response: $login_resp"

me_resp=$(curl -sf "http://127.0.0.1:${WEB_HOST_PORT}/api/auth/me" \
  -H "Authorization: Bearer $token") \
  || fail "GET /api/auth/me failed"

echo "$me_resp" | grep -q "${OperatorSeed__Email}" \
  || fail "/me response did not echo seeded operator email"
ok "login + /api/auth/me round-trip"

# -----------------------------------------------------------------------------
# AC-4: cross-project DNS on the shared devtools-infra network.
#
# A throwaway alpine/curl container attached to the network proves the same
# claim as exec-ing into a sibling project (DevHub is reachable by name from
# any container on devtools-infra), and doesn't require sibling images to
# ship curl/wget. Pulls alpine/curl on first run; subsequent runs are local.
# -----------------------------------------------------------------------------
info "AC-4: cross-project DNS to devhub-api on the shared network"

docker run --rm --network devtools-infra alpine/curl:latest \
  -sf http://devhub-api:8080/health >/dev/null \
  || fail "container on devtools-infra could not reach http://devhub-api:8080/health"
ok "container on devtools-infra reaches http://devhub-api:8080/health"

# -----------------------------------------------------------------------------
# Done.
# -----------------------------------------------------------------------------
printf '\n'
ok "umbrella smoke checks passed"
printf '    SPA:           http://127.0.0.1:%s/\n' "$WEB_HOST_PORT"
printf '    API health:    http://127.0.0.1:%s/health\n' "$API_HOST_PORT"
printf '\n'
printf 'Leaving the stack up. Tear down with:\n'
printf '    docker compose -f %s down\n' "$COMPOSE_FILE"
printf '  or, for the full umbrella, from the umbrella root:\n'
printf '    ./stop.sh\n'
