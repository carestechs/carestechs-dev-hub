# Implementation Plan: T-053 — Sibling-repo patches (../infra + ../start.sh)

## Task Reference
- **Task ID:** T-053 · **Type:** DevOps · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-1 + AC-5. The DevHub-side compose can't bring up the API if the `devhub` database doesn't exist; the umbrella's `./start.sh` can't bring DevHub up if it isn't in the `PROJECTS` list.

## Overview
Two one-line patches in two sibling repos. This plan documents the exact patch text and the merge order so the operator (or future reviewer) can land them without ambiguity. The DevHub repo itself doesn't change in this task — `docs/umbrella-adaptation.md` already contains the source-of-truth instructions.

## Repos to touch

### Patch 1: `../infra/init-databases.sql`

**Goal:** Append a `CREATE DATABASE` for `devhub` so a fresh infra volume materializes the database on first init.

**Patch text (sibling repo `../infra`):**
```sql
-- Append to init-databases.sql
CREATE DATABASE devhub;
```

(If the sibling repo already groups creates by owner, follow that convention. The DB owner is the default `devtools` user — no separate `CREATE ROLE` needed.)

**Commit message:**
```
feat(devhub): add devhub database to shared cluster

DevHub joins the umbrella per its FEAT-007 plan
(../carestechs-dev-hub/docs/umbrella-adaptation.md).
```

**PR description:** link this DevHub plan; link `docs/work-items/FEAT-007-umbrella-shared-infra.md`; flag that `init-databases.sql` runs only on first volume init and reference the manual one-shot for already-initialized hosts.

### Patch 2: `../start.sh` (umbrella root)

**Goal:** Add `carestechs-dev-hub` to the `PROJECTS=( ... )` array so `./start.sh` and `./stop.sh` cover DevHub alongside the other projects.

**Patch text (sibling umbrella root):**
```bash
PROJECTS=(
    carestechs-agent-orchestrator
    carestechs-flow-engine
    carestechs-agent-orchestrator-ui
    carestechs-dev-hub
)
```

**Commit message:**
```
feat: include carestechs-dev-hub in start.sh PROJECTS

Per ../carestechs-dev-hub/docs/work-items/FEAT-007-umbrella-shared-infra.md.
```

**PR description:** confirm `stop.sh` derives its iteration from the same `PROJECTS` array (no separate patch needed).

### Manual one-shot (for already-initialized infra volumes)

The DevHub-side README umbrella section (added in T-051) documents:
```bash
docker exec -i postgres psql -U devtools -d postgres -c 'CREATE DATABASE devhub;'
```

This is mandatory on any host where the `infra/` Postgres volume was created before Patch 1 merged. `init-databases.sql` is NOT re-run on existing volumes.

## Merge order

1. **T-051 lands first** in this repo. The new `docker-compose.prod.yml` references the shared `postgres` container; until it's merged, the sibling patches would still work (they're just adding lines), but no one would consume them yet.
2. **Patch 1 (`../infra/init-databases.sql`)** lands next. Fresh installs now auto-create `devhub`.
3. **Patch 2 (`../start.sh`)** lands last. Once it's in, `./start.sh` brings DevHub up alongside the other projects.

If patches 1 + 2 land before T-051 in this repo, no harm — `start.sh` will try to bring DevHub up via this repo's current compose, which doesn't reference the shared network at all, so it'll boot in its old standalone mode (or fail). Recommend the order above to keep semantics aligned.

## Files Affected
| File | Repo | Action |
|------|------|--------|
| `init-databases.sql` | `../infra` | Modify (append one line) |
| `start.sh` | umbrella root | Modify (one array entry) |
| `docs/umbrella-adaptation.md` | this repo | Verify (one-shot already documented) |

## Edge Cases & Risks
- **PR doesn't merge in sibling repo.** This is the dependency we can't enforce from inside DevHub. T-054's smoke script asserts the `devhub` database exists before bringing the API up — that's the safety net.
- **Operator runs `./start.sh` against unmerged sibling state.** `start.sh` simply iterates the `PROJECTS` array; if `carestechs-dev-hub` isn't in it, DevHub is silently skipped. Loud failure mode is preferred: T-051's README umbrella section explicitly says "list this repo in `../start.sh`."
- **Sibling repos may have CI / review processes.** Document the link to FEAT-007 in the PR description so reviewers can trace the request back to this work item.

## Acceptance Verification
- [ ] PR opened against `../infra` with the one-line patch.
- [ ] PR opened against the umbrella root with the `PROJECTS` array addition.
- [ ] DevHub's `docs/umbrella-adaptation.md` has the manual one-shot documented (already present — verify).
- [ ] After both sibling PRs merge, AC-1 + AC-5 from the FEAT-007 brief are exercisable by an operator following the runbook.
