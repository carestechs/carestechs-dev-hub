# Implementation Plan: T-092 — IMP-001 verification & docs sweep

## Task Reference
- **Task ID:** T-092
- **Type:** Testing
- **Workflow:** standard
- **Complexity:** S
- **Dependencies:** T-091 (and transitively T-090).
- **Rationale:** Closes out IMP-001 by verifying the eight success criteria in §9 against running services, updating `docs/ui-specification.md` per `CLAUDE.md`'s Documentation Maintenance Discipline, and flipping the work item to `Completed`.

## Overview
T-090 ships `boundExecutorProtocol` on `ProjectDto`. T-091 ships the per-protocol example payload in the Start Work modal and wires the parent to forward the field. This task is end-to-end verification across the three binding states (orchestrator / devhub / unbound), the UI-spec update, and work-item closeout.

## Implementation Steps

### Step 1: Verify the API response shape
**File:** N/A (manual verification)
**Action:** Verify

- Bring up the stack (`docker compose up -d` + `dotnet run --project src/DevHub.Api` + `cd client && ng serve` for solo-dev, or `./start.sh` for umbrella).
- Seed (or use existing) projects in three binding states:
  - A: bound to an orchestrator-protocol executor.
  - B: bound to a devhub-protocol executor (e.g. FakeExecutor).
  - C: no active binding.
- Run:
  ```sh
  curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8090/api/projects/by-slug/<slug> | jq '.data.boundExecutorProtocol'
  ```
  for each. Expect `"orchestrator"`, `"devhub"`, `null` respectively.
- Run the same against `/api/projects/{id}`. Same expectations.
- Run `curl -s ... /api/projects | jq '.data[].boundExecutorProtocol'`. Expect `null` for every row.

### Step 2: Verify the modal behavior across binding states
**File:** N/A (manual verification)
**Action:** Verify

- Log in as an operator in the SPA (`http://127.0.0.1:4300` umbrella / `http://localhost:4200` solo-dev).
- For each of projects A, B, C:
  - Navigate to project home; click *Start work*.
  - Observe the JSON textarea's initial value and `placeholder` attribute.
  - **A (orchestrator):** expect the multi-line `{ "task": "Describe the task to run" }` example.
  - **B (devhub):** expect `'{}'`.
  - **C (unbound):** expect the orchestrator fallback (per IMP-001 §9 — safe default).
- Edit the task value on project A to a real description; submit. Confirm the work item is created and routed to its detail page.
- Open the modal on project A again; submit *without* editing. Confirm the request is sent (browser devtools → Network) and the orchestrator's validation response (if any) surfaces in the existing `serverError` banner.

### Step 3: Update `docs/ui-specification.md`
**File:** `docs/ui-specification.md`
**Action:** Modify

- Locate the section that describes the Start Work modal (search for `"Start work"` / `"Input (JSON)"`). If the section does not yet exist, add a brief one under the Project Home / Work Items area.
- Document:
  - The textarea is pre-filled with a per-protocol example payload (and matching `placeholder`).
  - Today's defaults: `orchestrator` → `{ "task": "Describe the task to run" }`, `devhub` → `{}`.
  - The example is selected from `project.boundExecutorProtocol`; `null` falls back to the orchestrator example.
  - The wire `StartWorkItemRequest` shape is unchanged — this is purely a UX hint.
- Append a changelog row at the bottom of the file:
  ```
  | 2026-05-19 | IMP-001 | Start Work modal ships a per-protocol example payload as the textarea's initial value and placeholder, selected from `project.boundExecutorProtocol`. No wire DTO change. |
  ```

### Step 4: Flip IMP-001 status
**File:** `docs/work-items/IMP-001-start-work-input-placeholder.md`
**Action:** Modify

- In §1, set **Status** to `Completed`.
- No new follow-on note is required; the scope-expansion note added during T-090 already documents how the IMP grew during task generation.

### Step 5: Close-out
**File:** N/A
**Action:** Verify

- Confirm `dotnet test` is green on the branch tip.
- Confirm `cd client && ng test` is green.
- Confirm all IMP-001 §9 success criteria items are observably true.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `docs/ui-specification.md` | Modify | Document per-protocol example payload behavior; append changelog row. |
| `docs/work-items/IMP-001-start-work-input-placeholder.md` | Modify | Status → Completed. |

## Edge Cases & Risks

- **No matching section in `ui-specification.md` yet.** If the Start Work modal isn't documented there, add a minimal section (a few bullets) rather than a full screen spec — this IMP is too small to justify a new top-level entry.
- **Changelog format drift.** Confirm the exact column order against an existing entry at the bottom of `ui-specification.md` before appending.
- **Smoke environment unavailable.** If neither umbrella nor solo-dev is currently runnable on the verifier's machine, fall back to the Karma + integration-test evidence from T-090 and T-091 and document the substitution in the closeout comment.
- **Devhub-protocol project unavailable.** If no project is currently bound to a devhub-protocol executor (FakeExecutor not registered in this env), seed one as part of verification rather than skipping case B — that case is one of the IMP-001 §9 success criteria.

## Acceptance Verification

- [ ] API response shape verified for all three binding states (A/B/C) on single-load endpoints.
- [ ] API response shape verified for the list endpoint (always `null`).
- [ ] Modal behavior verified for all three binding states.
- [ ] Submitting an unedited orchestrator example produces a valid `POST /api/projects/{pid}/work-items` (devtools Network tab).
- [ ] `docs/ui-specification.md` updated + changelog row appended.
- [ ] `docs/work-items/IMP-001-start-work-input-placeholder.md` Status is `Completed`.
- [ ] `dotnet test` green, `cd client && ng test` green.
