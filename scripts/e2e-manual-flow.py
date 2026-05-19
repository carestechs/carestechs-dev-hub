#!/usr/bin/env python3
"""
scripts/e2e-manual-flow.py — end-to-end smoke for the DevHub manual flow.

Runs the seeded operator through:
  login → ensure team → ensure orchestrator-protocol executor + binding
        → ensure project → start work item → poll + auto-signal checkpoints
        → assert terminal Completed status.

The executor it registers points at the umbrella's carestechs-agent-orchestrator
(protocol="orchestrator", agentRef="lifecycle-agent@0.4.0-manual"). The flow is
LLM-driven, so total runtime is on the order of minutes and not bit-deterministic.

Modes (--mode):
  happy-path  Single auto-generated task, every checkpoint approved with
              minimal payload. Fastest smoke (~1 min).
  full        (default) Multi-task — tasks-confirmed payload overrides the
              agent's generated list with two explicit tasks — AND a reject
              path — the first review-completed signal carries
              verdict=fail with corrective feedback, forcing a single
              correction loop before the eventual pass. Exercises per-task
              looping, taskId routing, and the corrections budget.

Prerequisites (the script verifies these and fails fast otherwise):
  * Umbrella up: postgres, devhub-api, devhub-web, orchestrator-api all healthy.
  * devhub-api has env var ORCHESTRATOR_API_KEY set to the same secret as the
    orchestrator's ORCHESTRATOR_API_KEY (DevHub resolves credentialsRef by
    reading an env var of that name from its own process).

Stdlib-only (urllib + json) — no pip deps.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
import uuid
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any
from urllib import error as urlerror
from urllib import request as urlrequest

ROOT = Path(__file__).resolve().parent.parent

# ---------------------------------------------------------------------------
# Config (env-overridable for ops convenience).
# ---------------------------------------------------------------------------

ENV_FILE = ROOT / ".env"
DEFAULT_BASE = "http://127.0.0.1:8090"          # devhub-api loopback
DEFAULT_ORCH_INTERNAL = "http://orchestrator-api:8000"   # devtools-infra DNS
DEFAULT_AGENT_REF = "lifecycle-agent@0.4.0-manual"
DEFAULT_CREDS_REF = "ORCHESTRATOR_API_KEY"

POLL_INTERVAL_S = 4.0
TIMEOUT_S = 25 * 60                              # 25 min — LLM-driven runs
STUCK_AT_SAME_CHECKPOINT_S = 5 * 60              # bail if no progress

TERMINAL_STATUSES = {"Completed", "Failed", "Cancelled"}

# Checkpoint contracts to register on the executor. The orchestrator's
# CheckpointDerivation turns lifecycle-agent confirm_<X> pause nodes into
# <X>-confirmed signal names; we declare those + the two hand-mapped ones.
# All requiredRoleKey values must be roles that exist in DevHub — only
# "operator" is seeded, and the seeded operator is workspace-operator anyway
# (bypasses role checks), so we point everything at "operator".
CHECKPOINT_CONTRACTS: list[dict[str, Any]] = [
    {"checkpointKey": "brief-confirmed",       "displayName": "Confirm brief",
     "requiredRoleKey": "operator", "allowedOutcomes": ["approve"], "perTask": False},
    {"checkpointKey": "tasks-confirmed",       "displayName": "Confirm tasks",
     "requiredRoleKey": "operator", "allowedOutcomes": ["approve"], "perTask": False},
    {"checkpointKey": "assignment-confirmed",  "displayName": "Confirm assignment",
     "requiredRoleKey": "operator", "allowedOutcomes": ["approve"], "perTask": True},
    {"checkpointKey": "plan-confirmed",        "displayName": "Confirm plan",
     "requiredRoleKey": "operator", "allowedOutcomes": ["approve"], "perTask": True},
    {"checkpointKey": "implementation-complete","displayName": "Implementation complete",
     "requiredRoleKey": "operator", "allowedOutcomes": ["approve"], "perTask": True},
    {"checkpointKey": "review-completed",      "displayName": "Review completed",
     "requiredRoleKey": "operator", "allowedOutcomes": ["approve"], "perTask": True},
]

# The tasks `full` mode forces at tasks-confirmed (overriding whatever the LLM
# generated). Two short, obviously-distinct tasks so the per-task loop runs
# twice. Schema = lifecycle_manual_patches._TaskInput (id + title required).
MULTI_TASK_OVERRIDE: list[dict[str, str]] = [
    {"id": "t1", "title": "First smoke task: write a hello-world function."},
    {"id": "t2", "title": "Second smoke task: write a goodbye-world function."},
]


@dataclass
class RunState:
    """Per-run mutable state consulted by build_signal_payload."""
    mode: str                              # "happy-path" or "full"
    review_rejections_sent: int = 0        # how many fail verdicts so far
    review_rejections_target: int = 0      # how many fails to send before passing


def build_signal_payload(checkpoint_key: str, task_id: str | None,
                         state: RunState) -> tuple[str, dict[str, Any]]:
    """Pick the (outcome, payload) for a given signal in the current run mode.

    Returns (outcome, payload). The DevHub-level outcome stays "approve"
    throughout — DevHub's checkpoint contracts declare that as the only allowed
    outcome, and the orchestrator ignores it anyway. Reject semantics for the
    *agent* live in the payload (`verdict: fail` at review-completed).
    """
    if checkpoint_key == "assignment-confirmed":
        payload: dict[str, Any] = {"assignee": "operator"}
        if task_id is not None:
            payload["task_id"] = task_id
        return "approve", payload

    if checkpoint_key == "tasks-confirmed" and state.mode == "full":
        return "approve", {"tasks": MULTI_TASK_OVERRIDE}

    if checkpoint_key == "review-completed":
        if state.review_rejections_sent < state.review_rejections_target:
            state.review_rejections_sent += 1
            return "approve", {
                "verdict": "fail",
                "feedback": "Smoke-test reject: please tighten the implementation.",
            }
        return "approve", {"verdict": "pass"}

    return "approve", {}


# ---------------------------------------------------------------------------
# Tiny terminal helpers.
# ---------------------------------------------------------------------------

def info(msg: str) -> None: print(f"\033[1;34m==>\033[0m {msg}", flush=True)
def ok(msg: str)   -> None: print(f"\033[32m✓\033[0m {msg}", flush=True)
def warn(msg: str) -> None: print(f"\033[33m!\033[0m {msg}", flush=True)
def die(msg: str)  -> None:
    print(f"\033[31m✗\033[0m {msg}", file=sys.stderr, flush=True)
    sys.exit(1)


# ---------------------------------------------------------------------------
# .env loader (subset of `set -a; source`, only KEY=VALUE lines).
# ---------------------------------------------------------------------------

def load_env_file(path: Path) -> dict[str, str]:
    env: dict[str, str] = {}
    if not path.is_file():
        return env
    for line in path.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, v = line.split("=", 1)
        env[k.strip()] = v.strip().strip('"').strip("'")
    return env


# ---------------------------------------------------------------------------
# HTTP client.
# ---------------------------------------------------------------------------

class Http:
    def __init__(self, base: str) -> None:
        self.base = base.rstrip("/")
        self.token: str | None = None

    def request(self, method: str, path: str, body: Any | None = None,
                expect: tuple[int, ...] | None = None) -> tuple[int, Any]:
        url = self.base + path if path.startswith("/") else f"{self.base}/{path}"
        data = None
        headers = {"Accept": "application/json"}
        if body is not None:
            data = json.dumps(body).encode()
            headers["Content-Type"] = "application/json"
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        req = urlrequest.Request(url, data=data, method=method, headers=headers)
        try:
            with urlrequest.urlopen(req, timeout=30) as resp:
                raw = resp.read()
                status = resp.status
        except urlerror.HTTPError as e:
            raw = e.read()
            status = e.code
        payload: Any
        if raw:
            try:
                payload = json.loads(raw)
            except json.JSONDecodeError:
                payload = raw.decode(errors="replace")
        else:
            payload = None
        if expect is not None and status not in expect:
            raise RuntimeError(f"{method} {path} → {status}; body={payload!r}")
        return status, payload


def envelope_data(payload: Any) -> Any:
    """DevHub wraps responses in {data: ...}; unwrap, falling back to the raw value."""
    if isinstance(payload, dict) and "data" in payload:
        return payload["data"]
    return payload


# ---------------------------------------------------------------------------
# Preflight.
# ---------------------------------------------------------------------------

def preflight(env: dict[str, str], base: str) -> None:
    info(f"preflight: devhub-api at {base}")
    h = Http(base)
    status, _ = h.request("GET", "/health")
    if status != 200:
        die(f"GET {base}/health → {status} (is the umbrella up?)")
    ok("devhub-api /health OK")

    # Verify ORCHESTRATOR_API_KEY is wired into devhub-api's process env.
    info("preflight: devhub-api has ORCHESTRATOR_API_KEY in its environment")
    try:
        r = subprocess.run(
            ["docker", "exec", "devhub-api", "printenv", DEFAULT_CREDS_REF],
            capture_output=True, text=True, timeout=10,
        )
    except FileNotFoundError:
        die("docker CLI not found — needed to verify devhub-api env")
    if r.returncode != 0 or not r.stdout.strip():
        die(
            f"devhub-api process does not have env var {DEFAULT_CREDS_REF}.\n"
            "  DevHub resolves an executor's credentialsRef by reading an env var\n"
            "  of that name from its own process. Without this, signal forwarding\n"
            "  to the orchestrator will 401.\n\n"
            "  Wire it through docker-compose.prod.yml under `services.api.environment`:\n"
            "      ORCHESTRATOR_API_KEY: \"${ORCHESTRATOR_API_KEY}\"\n"
            "  …then add ORCHESTRATOR_API_KEY=<same-secret-as-orchestrator/.env>\n"
            "  to .env, and restart from the umbrella root (./stop.sh && ./start.sh)."
        )
    ok(f"{DEFAULT_CREDS_REF} is set inside devhub-api")

    info("preflight: orchestrator-api reachable from devhub-api on devtools-infra")
    r = subprocess.run(
        ["docker", "exec", "devhub-api", "wget", "-qO-", f"{DEFAULT_ORCH_INTERNAL}/health"],
        capture_output=True, text=True, timeout=10,
    )
    if r.returncode != 0:
        die(f"devhub-api cannot reach {DEFAULT_ORCH_INTERNAL}/health — orchestrator down?")
    ok("orchestrator-api reachable on devtools-infra")


# ---------------------------------------------------------------------------
# Idempotent get-or-create helpers.
# ---------------------------------------------------------------------------

def login(h: Http, email: str, password: str) -> dict[str, Any]:
    info(f"login as {email}")
    _, body = h.request("POST", "/api/auth/login",
                        {"email": email, "password": password},
                        expect=(200,))
    data = envelope_data(body)
    h.token = data["accessToken"]
    ok(f"logged in; memberId={data['member']['id']}")
    return data["member"]


def ensure_team(h: Http, name: str) -> str:
    _, body = h.request("GET", "/api/teams", expect=(200,))
    items = envelope_data(body)
    items = items.get("items", items) if isinstance(items, dict) else items
    for t in items or []:
        if t.get("name") == name:
            ok(f"team '{name}' already exists ({t['id']})")
            return t["id"]
    _, body = h.request("POST", "/api/teams", {"name": name}, expect=(200, 201))
    tid = envelope_data(body)["id"]
    ok(f"created team '{name}' ({tid})")
    return tid


def ensure_executor(h: Http, key: str) -> str:
    _, body = h.request("GET", "/api/admin/executors", expect=(200,))
    items = envelope_data(body)
    items = items.get("items", items) if isinstance(items, dict) else items
    for e in items or []:
        if e.get("key") == key:
            ok(f"executor '{key}' already registered ({e['id']})")
            return e["id"]

    req = {
        "key": key,
        "displayName": "Lifecycle Agent @ 0.4.0 manual (e2e)",
        "baseUrl": DEFAULT_ORCH_INTERNAL,
        "credentialsRef": DEFAULT_CREDS_REF,
        "protocol": "orchestrator",
        "checkpointContracts": CHECKPOINT_CONTRACTS,
    }
    _, body = h.request("POST", "/api/admin/executors", req, expect=(200, 201))
    eid = envelope_data(body)["id"]
    ok(f"registered executor '{key}' ({eid})")
    return eid


def ensure_binding(h: Http, project_type: str, executor_id: str) -> str:
    _, body = h.request("GET", "/api/admin/executor-bindings", expect=(200,))
    items = envelope_data(body)
    items = items.get("items", items) if isinstance(items, dict) else items
    for b in items or []:
        if b.get("projectType") == project_type:
            ok(f"binding for projectType={project_type} already exists ({b['id']})")
            return b["id"]
    _, body = h.request("POST", "/api/admin/executor-bindings",
                        {"projectType": project_type, "executorId": executor_id},
                        expect=(200, 201))
    bid = envelope_data(body)["id"]
    ok(f"bound projectType={project_type} → executor {executor_id} ({bid})")
    return bid


def ensure_project(h: Http, slug: str, name: str, project_type: str, team_id: str) -> str:
    status, body = h.request("GET", f"/api/projects/by-slug/{slug}")
    if status == 200:
        pid = envelope_data(body)["id"]
        ok(f"project '{slug}' already exists ({pid})")
        return pid
    req = {
        "name": name,
        "slug": slug,
        "projectType": project_type,
        "owningTeamId": team_id,
        "description": "e2e manual-flow smoke",
    }
    _, body = h.request("POST", "/api/projects", req, expect=(200, 201))
    pid = envelope_data(body)["id"]
    ok(f"created project '{slug}' ({pid})")
    return pid


# ---------------------------------------------------------------------------
# Work-item flow.
# ---------------------------------------------------------------------------

def start_work_item(h: Http, project_id: str, title: str) -> dict[str, Any]:
    # The orchestrator's RunIntakeWorkItem requires `id` (FEAT-N / BUG-N / IMP-N),
    # `kind` (FEAT|BUG|IMP), and `content` (opaque string — typically the brief
    # body). DevHub's OrchestratorExecutorClient pulls these from input when
    # present and otherwise synthesizes defaults from the correlation marker.
    intake_id = f"FEAT-{int(time.time()) % 1_000_000}"
    body = {
        "title": title,
        "input": {
            "id": intake_id,
            "kind": "FEAT",
            "content": (
                f"# {title}\n\n"
                "Smoke test work item driven by e2e-manual-flow.py.\n"
            ),
        },
    }
    _, resp = h.request("POST", f"/api/projects/{project_id}/work-items", body,
                        expect=(200, 201))
    wi = envelope_data(resp)
    ok(f"started work item {wi['id']} (status={wi['currentStatus']})")
    return wi


def get_work_item(h: Http, project_id: str, work_item_id: str) -> dict[str, Any]:
    _, body = h.request("GET", f"/api/projects/{project_id}/work-items/{work_item_id}",
                        expect=(200,))
    return envelope_data(body)


def signal(h: Http, project_id: str, work_item_id: str, checkpoint_key: str,
           task_id: str | None, state: RunState) -> dict[str, Any]:
    outcome, payload = build_signal_payload(checkpoint_key, task_id, state)
    body: dict[str, Any] = {"outcome": outcome, "payload": payload}
    if task_id is not None:
        body["taskId"] = task_id
    info(f"  signal {checkpoint_key}"
         f"{' (task='+task_id+')' if task_id else ''} "
         f"outcome={outcome} payload={payload}")
    status, resp = h.request("POST",
        f"/api/projects/{project_id}/work-items/{work_item_id}/checkpoints/{checkpoint_key}/signal",
        body)
    if status not in (200, 201):
        raise RuntimeError(f"signal {checkpoint_key} failed: {status} {resp!r}")
    return envelope_data(resp)


def drive_to_terminal(h: Http, project_id: str, work_item_id: str,
                      state: RunState) -> dict[str, Any]:
    """Poll the work item and signal each checkpoint visit until terminal.

    Re-entry aware: the same (checkpointKey, taskId) pair can appear more than
    once across the run (the reject path loops review-completed → request_impl
    → review-completed). A "visit" is bounded by a transition through
    status=Running; we re-arm the signal trigger whenever we observe a Running
    snapshot between two Waiting snapshots.
    """
    info(f"polling work item every {POLL_INTERVAL_S}s up to {TIMEOUT_S//60} min "
         f"(mode={state.mode}, "
         f"planned-rejects={state.review_rejections_target})")
    started = time.monotonic()
    signal_log: list[tuple[str, str | None]] = []
    per_pair_counts: dict[tuple[str, str | None], int] = defaultdict(int)
    last_signaled_visit: tuple[str, str | None] | None = None
    visit_rearmed = True      # first time we see any checkpoint, fire it
    same_checkpoint_since = started

    while True:
        elapsed = time.monotonic() - started
        if elapsed > TIMEOUT_S:
            die(f"timed out after {int(elapsed)}s without reaching terminal state")

        wi = get_work_item(h, project_id, work_item_id)
        status = wi["currentStatus"]
        cpk = wi.get("currentCheckpointKey")
        tid = wi.get("currentTaskId")

        if status in TERMINAL_STATUSES:
            ok(f"terminal: status={status} after {int(elapsed)}s, "
               f"{len(signal_log)} signal(s) across {len(per_pair_counts)} "
               f"unique (checkpoint, task) pair(s)")
            for (k, t), n in per_pair_counts.items():
                tag = f"(task={t})" if t else "(no task)"
                print(f"     · {k} {tag} × {n}", flush=True)
            return wi

        # Observing Running between two Waiting states means the agent has
        # advanced; arm the next signal even if the checkpoint key repeats.
        if status == "Running":
            visit_rearmed = True

        if cpk:
            now = time.monotonic()
            current_visit = (cpk, tid)
            fresh_pair = current_visit != last_signaled_visit
            if fresh_pair or visit_rearmed:
                signal(h, project_id, work_item_id, cpk, tid, state)
                signal_log.append(current_visit)
                per_pair_counts[current_visit] += 1
                last_signaled_visit = current_visit
                visit_rearmed = False
                same_checkpoint_since = now
            else:
                if now - same_checkpoint_since > STUCK_AT_SAME_CHECKPOINT_S:
                    die(f"stuck on {cpk} (task={tid}) for "
                        f"{int(now-same_checkpoint_since)}s — giving up")
        else:
            # Running but no checkpoint surfaced yet — keep polling.
            print(f"   …status={status} cpk=None elapsed={int(elapsed)}s",
                  flush=True)

        time.sleep(POLL_INTERVAL_S)


# ---------------------------------------------------------------------------
# Main.
# ---------------------------------------------------------------------------

@dataclass
class Config:
    base: str
    email: str
    password: str
    team_name: str
    project_slug: str
    project_name: str
    project_type: str
    executor_key: str
    work_item_title: str
    mode: str


def make_config(env: dict[str, str], mode: str) -> Config:
    base = os.environ.get("DEVHUB_BASE_URL", DEFAULT_BASE)
    email = env.get("OperatorSeed__Email") or "operator@devhub.local"
    password = env.get("OperatorSeed__Password") or ""
    if not password:
        die("OperatorSeed__Password missing from .env")

    run_tag = uuid.uuid4().hex[:6]
    return Config(
        base=base,
        email=email,
        password=password,
        team_name="e2e-smoke-team",
        project_slug="e2e-manual-flow",
        project_name="E2E Manual Flow",
        project_type="lifecycle-manual",
        executor_key=DEFAULT_AGENT_REF,
        work_item_title=f"E2E manual flow run {run_tag}",
        mode=mode,
    )


def parse_args(argv: list[str]) -> argparse.Namespace:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--mode", choices=["happy-path", "full"], default="full",
                   help="happy-path: single auto-task, all approve. "
                        "full (default): override with 2 explicit tasks AND "
                        "fail the first review for a correction loop.")
    return p.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv if argv is not None else sys.argv[1:])

    env = load_env_file(ENV_FILE)
    cfg = make_config(env, mode=args.mode)
    state = RunState(
        mode=cfg.mode,
        review_rejections_target=1 if cfg.mode == "full" else 0,
    )

    preflight(env, cfg.base)

    h = Http(cfg.base)
    login(h, cfg.email, cfg.password)

    team_id     = ensure_team(h, cfg.team_name)
    executor_id = ensure_executor(h, cfg.executor_key)
    ensure_binding(h, cfg.project_type, executor_id)
    project_id  = ensure_project(h, cfg.project_slug, cfg.project_name,
                                 cfg.project_type, team_id)

    wi = start_work_item(h, project_id, cfg.work_item_title)
    final = drive_to_terminal(h, project_id, wi["id"], state)

    if final["currentStatus"] != "Completed":
        die(f"final status was {final['currentStatus']}, not Completed")
    ok(f"e2e manual flow PASSED (mode={cfg.mode})")
    if cfg.mode == "full":
        if state.review_rejections_sent != state.review_rejections_target:
            warn(f"planned {state.review_rejections_target} review reject(s) "
                 f"but only sent {state.review_rejections_sent}")
        else:
            ok(f"reject path exercised: {state.review_rejections_sent} fail "
               f"verdict(s) sent before final pass")
    print(json.dumps({
        "mode": cfg.mode,
        "workItemId": final["id"],
        "status": final["currentStatus"],
        "executorRunId": final.get("executorRunId"),
        "reviewRejectionsSent": state.review_rejections_sent,
    }, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
