#!/usr/bin/env bash
#
# scripts/verify-docker.sh — legacy STANDALONE prod-stack smoke test.
#
# *** DO NOT run against the umbrella ***
#   This script was written for the pre-FEAT-007 standalone prod compose
#   (which shipped its own postgres). The current docker-compose.prod.yml
#   joins the umbrella's shared `devtools-infra` network and shared
#   `postgres` container — running this script against it will fail at
#   the postgres-healthcheck wait (no such service) and `down -v` would
#   tear down devhub-api/web mid-session if the umbrella is up.
#
# For the umbrella smoke test (no teardown), use scripts/verify-umbrella.sh
# (lands in T-054).
#
# Kept here for historical regression / fallback to a hypothetical
# standalone branch; new work should target verify-umbrella.sh.

set -euo pipefail

cd "$(dirname "$0")/.."

COMPOSE_FILE="docker-compose.prod.yml"
COMPOSE=(docker compose -f "$COMPOSE_FILE" --env-file .env.production)
WEB_PORT="${WEB_HOST_PORT:-8080}"
SEED_EMAIL="${OperatorSeed__Email:-operator@devhub.example.com}"
SEED_PASSWORD="${OperatorSeed__Password:-replace-me-and-rotate-immediately}"

if [[ ! -f .env.production ]]; then
  echo "==> .env.production not found, copying .env.production.example"
  cp .env.production.example .env.production
fi

# shellcheck disable=SC1091
set -a; source .env.production; set +a

cleanup() {
  echo "==> teardown"
  "${COMPOSE[@]}" down -v --remove-orphans > /dev/null 2>&1 || true
}
trap cleanup EXIT

echo "==> building images"
"${COMPOSE[@]}" build

echo "==> bringing the stack up"
"${COMPOSE[@]}" up -d

echo "==> waiting for api healthcheck"
deadline=$(( $(date +%s) + 180 ))
until docker inspect --format='{{.State.Health.Status}}' devhub-api 2>/dev/null | grep -q '^healthy$'; do
  if [[ $(date +%s) -ge $deadline ]]; then
    echo "[fail] api did not become healthy in 180s" >&2
    "${COMPOSE[@]}" logs api >&2 || true
    exit 1
  fi
  sleep 2
done

echo "==> waiting for web healthcheck"
deadline=$(( $(date +%s) + 60 ))
until docker inspect --format='{{.State.Health.Status}}' devhub-web 2>/dev/null | grep -q '^healthy$'; do
  if [[ $(date +%s) -ge $deadline ]]; then
    echo "[fail] web did not become healthy in 60s" >&2
    "${COMPOSE[@]}" logs web >&2 || true
    exit 1
  fi
  sleep 2
done

echo
echo "==> [1/3] GET /  (SPA index served by nginx)"
status=$(curl -sf -o /tmp/dch-smoke-spa.html -w '%{http_code}' "http://127.0.0.1:${WEB_PORT}/")
test "$status" = "200" || { echo "[fail] SPA root returned $status" >&2; exit 1; }
grep -q '<title>DevHub</title>' /tmp/dch-smoke-spa.html || { echo "[fail] SPA index missing <title>DevHub</title>" >&2; exit 1; }
rm -f /tmp/dch-smoke-spa.html
echo "    ok"

echo "==> [2/3] POST /api/auth/login (seeded operator)"
http_response=$(mktemp)
status=$(curl -sf -o "$http_response" -w '%{http_code}' \
  -X POST "http://127.0.0.1:${WEB_PORT}/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"${SEED_EMAIL}\",\"password\":\"${SEED_PASSWORD}\"}")
test "$status" = "200" || { echo "[fail] login returned $status" >&2; cat "$http_response" >&2; exit 1; }
ACCESS=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['data']['accessToken'])" "$http_response")
test -n "$ACCESS" || { echo "[fail] login response missing accessToken" >&2; exit 1; }
rm -f "$http_response"
echo "    ok (got ${#ACCESS}-char JWT)"

echo "==> [3/3] GET /api/auth/me (with bearer)"
me=$(mktemp)
status=$(curl -sf -o "$me" -w '%{http_code}' \
  -H "Authorization: Bearer ${ACCESS}" \
  "http://127.0.0.1:${WEB_PORT}/api/auth/me")
test "$status" = "200" || { echo "[fail] /me returned $status" >&2; cat "$me" >&2; exit 1; }
EMAIL=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['data']['member']['email'])" "$me")
test "$EMAIL" = "$SEED_EMAIL" || { echo "[fail] /me returned email=$EMAIL, want $SEED_EMAIL" >&2; exit 1; }
rm -f "$me"
echo "    ok (member.email=${EMAIL})"

echo
echo "==> all smoke checks passed"
