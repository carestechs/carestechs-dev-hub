# Feature Brief: FEAT-008 — Code-Source Binding (project repo + optional work branch)

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-008 |
| **Name** | Code-Source Binding (project repo + optional work branch) |
| **Target Version** | v1 |
| **Status** | Not Started |
| **Priority** | High |
| **Requested By** | Operator (orchestrator IMP-004 deprecation timer) |
| **Date Created** | 2026-05-17 |

## 2. User Story

**As an** operator, **I want to** record the GitHub repo and default branch on each DevHub project (and optionally an override work branch per work item), **so that** when a lifecycle executor accepts a start request it knows exactly which repo and branch to operate on — without DevHub depending on a deploy-time `repo_root` baked into the executor process.

## 3. Goal

Close the gap surfaced in the 2026-05-17 conversation: DevHub today has no notion of source code. The carestechs-agent-orchestrator's IMP-004 release added an `intake.codeSource` block on `POST /api/v1/runs` (`repo`, `baseBranch`, optional `workBranch`) and a deprecation window (`LIFECYCLE_CODE_SOURCE_REQUIRED=false`) during which omitting the block returns 202 + a `intake-code-source-missing-deprecated` warning. When the flag flips, missing or malformed `codeSource` becomes a hard 400.

This FEAT makes DevHub the source of truth for the repo coordinates and forwards them on every executor start.

## 4. Feature Scope

### 4.1 Included

- **`Project` schema extension**:
  - `repo` (string, nullable v1, `owner/name` shape, max 140 chars).
  - `default_branch` (string, nullable v1, max 200 chars).
  - EF migration in `DevHub.Modules.Workspace`.
- **`WorkItem` schema extension**:
  - `work_branch` (string, nullable, max 200 chars) — optional per-work-item override.
  - EF migration in `DevHub.Modules.WorkItems`.
- **Validation at the DevHub boundary** (mirrors the orchestrator's rules, executed in services before any forward):
  - `repo`: matches `^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$`, no `https://` prefix, no `.git` suffix, no whitespace, no leading `/`.
  - `default_branch` / `work_branch`: non-empty, no whitespace, no leading `/`, no `..`, no ASCII control chars.
  - Rejected values produce `400 ValidationException` problem details — never reach the executor.
- **DTO surface**:
  - `ProjectDto`, `CreateProjectRequest`, `UpdateProjectRequest` gain `repo` + `defaultBranch`.
  - `WorkItemDto`, `CreateWorkItemRequest`, `UpdateWorkItemRequest` gain `workBranch`.
- **Forward on start**: `WorkItemsService.StartAsync` builds an `intake.codeSource` block from the project's `repo` / `defaultBranch` (and the work item's `workBranch` if set) and includes it in the executor start payload alongside the existing `input` + `correlationMarker`. Field names follow the orchestrator spec exactly.
- **UI surface (operator-only edit)**:
  - Project create modal (FEAT just shipped): two new fields — `Repo (owner/name)`, `Default branch` — placed under Project type. Optional in v1.
  - Project detail page: read-only display of repo (linked to `https://github.com/<repo>`) and default branch; "Edit" affordance behind operator gate.
  - Work item create form / detail page: optional `Work branch override` field.
- **Audit**: writes to `repo`, `default_branch`, `work_branch` produce `project:update` / `workitem:update` audit entries with the before/after values in `details`.
- **Doc updates**:
  - `docs/data-model.md` (Project + WorkItem entities, changelog entry).
  - `docs/api-spec.md` (DTO additions, validation rules, changelog entry).
  - `docs/ui-specification.md` (Project create/detail screens, WorkItem detail, changelog entry).
  - `CLAUDE.md` pattern note on boundary-validation parity with executor contracts.

### 4.2 Excluded

- **Cloning, fetching, or any direct git operations from DevHub.** DevHub stores coordinates; the executor does the I/O.
- **GitHub App / token storage in DevHub.** Authentication to GitHub is the executor's concern (it already has its own credential model).
- **Linking back from work items to PR numbers, commit SHAs, or check-run IDs.** That is FEAT-010 territory (executor → DevHub callback), not this FEAT.
- **Multiple repos per project** or **monorepo sub-path bindings** (`working_subpath`). v1 = one repo per project, root path.
- **Auto-derivation of `workBranch` inside DevHub.** The spec explicitly says "a future executor" may derive it; DevHub only forwards what the user supplied.
- **Backfill of `repo` / `default_branch` on projects created before this FEAT.** They simply stay null and continue to trigger the orchestrator's deprecation warning until an operator edits them.

## 5. Acceptance Criteria

- **AC-1:** EF migrations add the three columns (`projects.repo`, `projects.default_branch`, `work_items.work_branch`) as nullable text. Existing rows survive the migration with `NULL` in the new columns.
- **AC-2:** `POST /api/projects` accepts `{ ..., "repo": "carestechs/your-repo", "defaultBranch": "main" }`. The created project's `ProjectDto` round-trips the new fields. Same for `PATCH /api/projects/{id}`.
- **AC-3:** Malformed `repo` (`"https://github.com/foo/bar"`, `"foo/bar.git"`, `"foo"`, `"foo/bar/baz"`) returns `400 application/problem+json` with `type: /probs/validation` and **does not** create or modify any project row. A `Denied` audit entry is written with the failed rule.
- **AC-4:** Malformed `defaultBranch` / `workBranch` (whitespace, `/main`, `..`, control chars) returns `400` with the same shape. Boundary-level — no executor call attempted.
- **AC-5:** Starting a work item whose project has `repo="acme/widgets"` and `default_branch="main"`, with `workBranch="feat/abc"` set, sends an executor start payload that includes:
  ```json
  { "intake": { "codeSource": { "repo": "acme/widgets", "baseBranch": "main", "workBranch": "feat/abc" } } }
  ```
  (alongside the existing `input` / `correlationMarker`). Verified with a fake-executor integration test that asserts the JSON body byte-for-byte on the relevant subtree.
- **AC-6:** Starting a work item whose project has `repo=NULL` sends **no** `codeSource` block (omitted entirely, not sent as `null`). This is the deprecation-window-compatible behavior — orchestrator returns 202 + warning, not 400. Logged at INFO with a `codeSourceMissing=true` field to aid grepping.
- **AC-7:** Starting a work item with `workBranch=NULL` sends `codeSource` without the `workBranch` field (omitted, not `null`).
- **AC-8:** Operator UI: from the Project create modal, an operator can set `repo` + `defaultBranch` at creation time; from the Project detail page, an operator can edit them later. Non-operator members see them as read-only.
- **AC-9:** UI: a work item's detail page shows the effective branch (`workBranch ?? project.defaultBranch ?? "(not set)"`), and an operator can edit `workBranch` from that page.
- **AC-10:** Every update to `repo`, `default_branch`, or `work_branch` produces a `Granted` audit entry with `details` containing both the previous and new values. Denied validation produces a `Denied` audit entry with the rejected value (truncated to 200 chars) and the rule name.

## 6. Key Entities and Business Rules

| Entity | Field | Rule |
|--------|-------|------|
| `Project` | `repo` | Optional v1. When set, must match `^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$`. Stored verbatim (no normalization). |
| `Project` | `default_branch` | Optional v1. When set, must be a valid branch shorthand (no whitespace / `/`-prefix / `..` / control chars). |
| `WorkItem` | `work_branch` | Optional. Same validation as `default_branch`. Overrides project-level default when set. |
| Forward contract | `intake.codeSource` | Field names + casing mirror orchestrator IMP-004 exactly. `workBranch` is **omitted** when null, not sent as `null` or `""`. |
| Backwards compatibility | Pre-FEAT projects | `repo`/`default_branch` remain null. Starts succeed today (orchestrator deprecation window) and will fail once the orchestrator flips `LIFECYCLE_CODE_SOURCE_REQUIRED=true`. Operators must edit those projects to set the fields before the flip. |

## 7. API Impact

- `POST /api/projects` — request gains optional `repo`, `defaultBranch`; response carries them.
- `PATCH /api/projects/{id}` — request gains optional `repo`, `defaultBranch`.
- `GET /api/projects` / `GET /api/projects/{id}` / `GET /api/projects/by-slug/{slug}` — response shape gains the fields.
- `POST /api/projects/{id}/work-items` — request gains optional `workBranch`; response carries it.
- `PATCH /api/projects/{id}/work-items/{wid}` — request gains optional `workBranch`.
- `POST /api/projects/{id}/work-items/{wid}/start` — no request change; downstream payload to the executor gains `intake.codeSource` derived from project + work item.

No new endpoints. No breaking changes to existing consumers.

## 8. UI Impact

- **Project create modal** (`project-form.modal.*`): adds two optional fields (`repo`, `defaultBranch`) under Project type.
- **Project detail page** (`project-home.page.*`): shows `Repo` (linked to GitHub) and `Default branch` in the metadata strip; "Edit" opens an inline modal (operator-only). Read-only view for non-operators.
- **Work item detail page** (`work-item-detail.page.*`): shows the effective branch row; operators can edit `workBranch` via an inline field.
- **Empty-state copy** on the Project page header: when an operator opens a project whose `repo` is null, a soft warning banner appears: "No repo set on this project — once the orchestrator flips the strict flag, starting work items will fail. Set `repo` + `default branch` to fix."

## 9. Edge Cases

- **Project created before this FEAT, never edited.** `repo`/`default_branch` stay null. Today: 202 + warning from orchestrator. After flag flip: hard 400 from orchestrator → DevHub façade surfaces 502 with the executor's problem detail. Documented; not auto-backfilled.
- **Operator sets `repo` but not `default_branch`** (or vice versa). Both are independently optional; the forward sends whichever subset is set. Orchestrator will reject if `baseBranch` is missing, so the UI shows a soft warning when only one of the pair is filled.
- **Operator typo (`https://github.com/foo/bar`).** Caught by DevHub boundary validation — never reaches the executor, no audit-Denied for the executor side.
- **Branch name containing legitimate `/`** (`feat/imp-042`). Allowed — only **leading** `/` is rejected.
- **Trailing whitespace.** Rejected. We do not trim; the operator must enter clean values.
- **Renaming a repo on GitHub.** DevHub stores the old name until an operator edits it. No detection. Documented.
- **Work item whose project's `repo` changes after the work item is started.** No replay — the in-flight run sticks with whatever was sent at start time. New starts pick up the new value. Audit trail records both the project-update and any subsequent starts.

## 10. Constraints

- **Validation parity with the executor.** Boundary validation in DevHub MUST be at least as strict as the orchestrator's; we never want to forward a payload that the executor will 400 on. If the orchestrator tightens its rules, DevHub follows in the same PR.
- **No schema-required fields in v1.** Making `repo` / `default_branch` required on `Project` would break the create modal we just shipped and force backfill of every existing test fixture. The strictness lives in the orchestrator's deprecation-flag flip, not in the DevHub schema.
- **Field names match the executor spec exactly** (`codeSource`, `baseBranch`, `workBranch`, `repo`). No DevHub-side renaming.
- **Audit captures values, not secrets.** `repo` and branch names are not secrets, but we still truncate to 200 chars in audit `details` to bound log size.

## 11. Motivation and Priority Justification

**Motivation:** The orchestrator's IMP-004 release is the first concrete cross-system signal that source-code identity belongs in DevHub. Without this FEAT, DevHub has no way to participate in a multi-repo orchestrator deployment — every project would resolve to the executor's process-wide `repo_root`. Beyond the deprecation timer, this FEAT is what makes the operator's mental model match reality: "this DevHub project = this GitHub repo."

**Impact if delayed:** Every `workitem:start` on a project with no `codeSource` will start 400-failing once the orchestrator flips `LIFECYCLE_CODE_SOURCE_REQUIRED=true`. The deprecation window is the migration runway. The longer this FEAT slips, the more existing projects need backfill at flip time.

**Dependencies on this feature:** FEAT-009 (assignment-confirmed pause) is independent. FEAT-010 (VCS callbacks from executor: PR number, head SHA) depends on this FEAT for the WorkItem table to have a place to hang the callback fields naturally next to `work_branch`.

## 12. Traceability

| Reference | Link |
|-----------|------|
| **Persona** | Operator |
| **Stakeholder Scope Item** | Org context owned by DevHub; executors stay headless |
| **Success Metric** | Zero `intake-code-source-missing-deprecated` warnings in orchestrator logs after rollout |
| **Related Work Items** | Enables FEAT-010 (executor → DevHub VCS callbacks). Parallel to FEAT-009 (per-task assignment pause). |
| **Upstream spec** | carestechs-agent-orchestrator IMP-004 + IMP-005 release notes (2026-05-17) |
| **Validation rules** | Boundary parity with `intake.codeSource` schema in orchestrator |
