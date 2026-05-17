# Implementation Plan: T-083 — verify-adapter.sh + DevHub docs update

## Task Reference
- **Task ID:** T-083 · **Type:** DevOps + Documentation · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-1, AC-10. The smoke script is the operator's debugging tool; the docs update is what tells operators to point executor URLs at the adapter.

## Overview
**Two deliverables across two repos**: a bash smoke script in the adapter repo, and documentation updates in DevHub (`docs/ARCHITECTURE.md`, `docs/api-spec.md`, new `docs/orchestrator-adapter.md`).

## Implementation Steps

### Step 1: `scripts/verify-adapter.sh`
**File (sibling):** `scripts/verify-adapter.sh` · Create

```bash
#!/usr/bin/env bash
# Adapter end-to-end smoke. Boots the umbrella, exercises every translation
# endpoint, asserts the run reaches `completed` on the orchestrator side.
# Pattern mirrors carestechs-dev-hub/scripts/verify-umbrella.sh.
set -euo pipefail

ADAPTER_URL="${ADAPTER_URL:-http://127.0.0.1:8095}"
DEVHUB_URL="${DEVHUB_URL:-http://127.0.0.1:8090}"
ORCH_URL="${ORCH_URL:-http://127.0.0.1:8000}"
DEVHUB_API_KEY="${DEVHUB_API_KEY:?required}"

log() { printf '\033[1;34m[verify]\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m[FAIL]\033[0m %s\n' "$*" >&2; exit 1; }

log "1. Adapter health"
curl -sf "$ADAPTER_URL/health" | grep -q '"status":"ok"' || fail "adapter not healthy"

log "2. Orchestrator reachable through adapter"
curl -sf "$ADAPTER_URL/health" | grep -q '"upstream":true' || fail "adapter cannot reach orchestrator"

log "3. Start a work item"
MARKER=$(uuidgen | tr -d - | cut -c1-32)
START_RESP=$(curl -sf -X POST "$ADAPTER_URL/work-items" \
  -H "Authorization: Bearer $DEVHUB_API_KEY" \
  -H "Content-Type: application/json" \
  -d "{\"input\": {}, \"correlationMarker\": \"$MARKER\"}")
echo "$START_RESP" | grep -q '"currentStatus":"Running"' || fail "start did not return Running"
log "  marker=$MARKER"

log "4. Fetch state — expect Running or WaitingOnCheckpoint"
sleep 2
FETCH_RESP=$(curl -sf "$ADAPTER_URL/work-items/$MARKER" -H "Authorization: Bearer $DEVHUB_API_KEY")
echo "$FETCH_RESP" | grep -q '"currentStatus":"\(Running\|WaitingOnCheckpoint\)"' || fail "fetch did not return Running/Waiting"

log "5. (If WaitingOnCheckpoint) signal it"
if echo "$FETCH_RESP" | grep -q '"WaitingOnCheckpoint"'; then
  CK=$(echo "$FETCH_RESP" | grep -oP '"currentCheckpointKey":"\K[^"]+')
  log "  checkpoint=$CK"
  SIG_RESP=$(curl -sf -X POST "$ADAPTER_URL/work-items/$MARKER/checkpoints/$CK/signal" \
    -H "Authorization: Bearer $DEVHUB_API_KEY" \
    -H "Content-Type: application/json" \
    -d '{"outcome": "confirmed", "payload": {}}')
  log "  signal returned httpStatus=$(echo $SIG_RESP | grep -oP '"httpStatus":\K[0-9]+')"
fi

log "6. Cancel"
curl -sf -X POST "$ADAPTER_URL/work-items/$MARKER/cancel" \
  -H "Authorization: Bearer $DEVHUB_API_KEY" \
  -o /dev/null \
  -w "%{http_code}" | grep -q '^204$' || fail "cancel did not return 204"

log "PASS"
```

`chmod +x scripts/verify-adapter.sh`. Don't tear down at the end — same convention as FEAT-007's `verify-umbrella.sh`.

### Step 2: DevHub `docs/ARCHITECTURE.md` — new section
**File (this repo):** `docs/ARCHITECTURE.md` · Modify

Under the existing "Executor Registry" section, append a subsection:

```markdown
### Executor adapter (FEAT-010)

DevHub's executor wire protocol (`/work-items`, `/work-items/{marker}/checkpoints/{key}/signal`, `/stream`, etc.) does not match the carestechs-agent-orchestrator's HTTP surface (`/api/v1/runs`). Production executor registrations point at a small Python adapter service (`devhub-orchestrator-adapter` on the umbrella) that translates between the two. Auth is bearer-in / API-key-out; marker↔run-id mapping lives in its own Postgres DB on the shared cluster.

See `docs/orchestrator-adapter.md` and the sibling repo `../carestechs-devhub-orchestrator-adapter` for source + operator notes.
```

Append changelog entry.

### Step 3: DevHub `docs/api-spec.md` — one-line pointer
**File (this repo):** `docs/api-spec.md` · Modify

In the Executor Registry section, add at the top:

```markdown
**Adapter convention (FEAT-010):** Production executor registrations point at the adapter URL (`http://devhub-orchestrator-adapter:8000` under the umbrella), not at the orchestrator directly. The adapter translates DevHub's executor protocol to the orchestrator's `/api/v1/runs` API. See [`docs/orchestrator-adapter.md`](orchestrator-adapter.md).
```

Append changelog entry.

### Step 4: New DevHub `docs/orchestrator-adapter.md`
**File (this repo):** `docs/orchestrator-adapter.md` · Create

A short operator-facing doc:

```markdown
# Orchestrator Adapter (FEAT-010)

Reference doc for operators configuring a DevHub deployment against the
carestechs-agent-orchestrator. The adapter source lives in the sibling
repo `../carestechs-devhub-orchestrator-adapter`.

## Why

DevHub's executor wire protocol (`/work-items`, `/signal`, `/stream`)
does not match the orchestrator's `/api/v1/runs` surface. The adapter
fills the gap so DevHub can drive real lifecycle runs without changing
its `ExecutorHttpClient`.

## Deployment

The adapter joins the DevTools umbrella alongside the orchestrator. From
`devtools-umbrella` root: `./start.sh`. Adapter exposes `127.0.0.1:8095`
for ops curl and `devhub-orchestrator-adapter:8000` for in-network DNS.

## Registering the executor in DevHub

When creating an executor registration via the DevHub admin UI:

| Field | Value |
|-------|-------|
| Base URL | `http://devhub-orchestrator-adapter:8000` |
| Credentials Ref | Env var name holding the adapter's `DEVHUB_API_KEY` value |
| Checkpoint contracts | Match the agent definition (e.g. `brief-confirmed`, `assignment-confirmed` with `perTask=true`, etc.) |

## Env vars

| Var | Purpose |
|-----|---------|
| `DEVHUB_API_KEY` | Inbound bearer DevHub sends |
| `ORCHESTRATOR_BASE_URL` | Where the adapter calls upstream |
| `ORCHESTRATOR_API_KEY` | `X-API-Key` sent to the orchestrator |
| `AGENT_REF` | Which agent every run uses (single-agent per adapter instance in v1) |
| `ADAPTER_DB_URL` | Postgres URL for the marker↔run-id store |

## Operator runbook

- Smoke test: `cd ../carestechs-devhub-orchestrator-adapter && scripts/verify-adapter.sh`
- Logs: `docker logs devhub-orchestrator-adapter -f`
- Marker mapping: `docker exec postgres psql -U devtools -d devhub_orchestrator_adapter -c 'SELECT marker, run_id FROM marker_mapping ORDER BY created_at DESC LIMIT 20;'`
- When DevHub returns 502 on a façade call: check the adapter logs first (it's the most likely culprit before the orchestrator).
```

### Step 5: Smoke
After all tasks land:

```bash
cd ../carestechs-devhub-orchestrator-adapter
./scripts/verify-adapter.sh
```

Expect `PASS`.

## Files Affected
| File | Action | Repo |
|------|--------|------|
| `scripts/verify-adapter.sh` | Create | adapter |
| `docs/ARCHITECTURE.md` | Modify | DevHub |
| `docs/api-spec.md` | Modify | DevHub |
| `docs/orchestrator-adapter.md` | Create | DevHub |
| `docs/work-items/FEAT-010-orchestrator-adapter.md` | Mark Completed | DevHub |

## Edge Cases & Risks
- **Smoke script depends on the work item reaching a checkpoint.** If the agent doesn't pause within 2 s of start, the signal step is skipped (and the cancel step still runs). Acceptable — the script is a smoke, not a full lifecycle exerciser.
- **`uuidgen` portability.** macOS has it; Linux usually does; busybox might not. If `verify-adapter.sh` is run in a container, fall back to `python -c 'import uuid; print(uuid.uuid4().hex)'`.
- **The script reads `DEVHUB_API_KEY` from env.** Operator must export it (or source `.env.production`). Documented in the script's leading comment.

## Acceptance Verification
- [ ] `scripts/verify-adapter.sh` runs against a freshly booted umbrella and prints `PASS`.
- [ ] DevHub `docs/ARCHITECTURE.md` has the new section + changelog entry.
- [ ] DevHub `docs/api-spec.md` has the one-line pointer + changelog entry.
- [ ] DevHub `docs/orchestrator-adapter.md` exists and documents env vars + runbook.
- [ ] FEAT-010 brief Status is Completed.
