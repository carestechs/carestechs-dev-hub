# Implementation Plan: T-090 — Expose `BoundExecutorProtocol` on `ProjectDto`

## Task Reference
- **Task ID:** T-090
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** S
- **Rationale:** The frontend modal in T-091 needs the bound executor's protocol so it can choose the right example payload. `IExecutorRouter` already exposes that mapping and is already injected into `ProjectService` — this task plumbs it onto the DTO. See IMP-001 §4 / §8 for scope bounds.

## Overview
Add a nullable `BoundExecutorProtocol` field to `ProjectDto`. Populate it on single-project loads (`Get`, `GetBySlug`, `Create`, `Update` — all funnel through `LoadAsync`) via `IExecutorRouter.ResolveAsync(projectId)`. Leave it `null` on `ListAsync` to avoid an N+1. Add three integration tests against the existing Workspace test harness and update `docs/api-spec.md` + `docs/data-model.md` with changelog rows.

## Implementation Steps

### Step 1: Add the field to `ProjectDto`
**File:** `src/DevHub.Modules.Workspace/DTOs/ProjectDtos.cs`
**Action:** Modify

- Append `string? BoundExecutorProtocol` to the positional record (after `CreatedAt`):
  ```csharp
  public sealed record ProjectDto(
      Guid Id,
      string Name,
      string Slug,
      string ProjectType,
      TeamRefDto OwningTeam,
      string? Description,
      string? Repo,
      string? DefaultBranch,
      int InFlightWorkItems,
      DateTimeOffset CreatedAt,
      string? BoundExecutorProtocol);
  ```
- Do not add validation attributes; this is a server-projected read-only field.

### Step 2: Project the value in `LoadAsync`
**File:** `src/DevHub.Modules.Workspace/Services/ProjectService.cs`
**Action:** Modify

- In `LoadAsync` (currently at `ProjectService.cs:233`), after the existing `row` projection and before the `return new ProjectDto(...)`, resolve the descriptor:
  ```csharp
  var descriptor = await router.ResolveAsync(row.Id, ct);
  ```
- Update the `return new ProjectDto(...)` call to pass `descriptor?.Protocol` as the trailing argument:
  ```csharp
  return new ProjectDto(
      row.Id, row.Name, row.Slug, row.ProjectType,
      new TeamRefDto(row.Team?.Id ?? row.OwningTeamId, row.Team?.Name ?? string.Empty),
      row.Description, row.Repo, row.DefaultBranch, InFlightWorkItems: 0, row.CreatedAt,
      BoundExecutorProtocol: descriptor?.Protocol);
  ```
- Do **not** wrap in try/catch. If the router throws, surface it — this is internal infrastructure, not a system boundary.
- Confirm `CreateAsync` (`ProjectService.cs:92`) and `UpdateAsync` (`ProjectService.cs:144`) return via `LoadAsync`. (`CreateAsync` does — reads `ProjectService.cs:233` is the only DTO construction path for single-project loads. Verify `UpdateAsync` does the same; if it constructs `ProjectDto` directly, route it through `LoadAsync` for parity.)

### Step 3: Pass `null` from `ListAsync`
**File:** `src/DevHub.Modules.Workspace/Services/ProjectService.cs`
**Action:** Modify

- Update the `rows.Select(r => new ProjectDto(...))` projection (currently at `ProjectService.cs:67-70`) to pass `BoundExecutorProtocol: null`:
  ```csharp
  var dtos = rows.Select(r => new ProjectDto(
      r.Id, r.Name, r.Slug, r.ProjectType,
      new TeamRefDto(r.Team?.Id ?? r.OwningTeamId, r.Team?.Name ?? string.Empty),
      r.Description, r.Repo, r.DefaultBranch, InFlightWorkItems: 0, r.CreatedAt,
      BoundExecutorProtocol: null)).ToList();
  ```
- No router call here. This is deliberate — list rows do not open the modal, and an N+1 across a page is not justified.

### Step 4: Add integration tests
**File:** `tests/DevHub.Modules.Workspace.Tests/EndpointsTests.cs` (or a new sibling test file if the existing one is already large — confirm during implementation)
**Action:** Modify

- Use the existing `Testcontainers` / `PostgresCollection` harness (`tests/DevHub.Modules.Workspace.Tests/PostgresCollection.cs`).
- Add three tests:
  1. **`GetBySlug_PopulatesBoundExecutorProtocol_WhenBindingExists`**: seed an `ExecutorRegistration` with `Protocol = "orchestrator"` and an `ExecutorBinding` for the test project's `projectType`; `GET /api/projects/by-slug/{slug}`; assert `boundExecutorProtocol == "orchestrator"`.
  2. **`Get_BoundExecutorProtocolIsNull_WhenNoBinding`**: create a project whose `projectType` has no `ExecutorBinding`; `GET /api/projects/{id}`; assert `boundExecutorProtocol == null`.
  3. **`List_BoundExecutorProtocolIsAlwaysNull`**: same fixture as test 1 (with a binding); `GET /api/projects`; assert every row's `boundExecutorProtocol == null`. This is the deliberate-N+1-avoidance contract.
- Use the existing `_operator` / `alice` test client patterns from `EndpointsTests.cs:80-217`. Search the file for how `ExecutorRegistration` / `ExecutorBinding` are seeded in existing FEAT-004 tests; reuse that helper.
- Do not mock `IExecutorRouter` — let the real implementation hit `ExecutorRegistryDbContext`. This is integration-test territory.

### Step 5: Update `docs/api-spec.md`
**File:** `docs/api-spec.md`
**Action:** Modify

- Locate § ProjectDto (line ~710 per current state). Add the new field row:
  ```
  | `boundExecutorProtocol` | string \| null | `'devhub'` \| `'orchestrator'` \| `null`. Resolved from the active `ExecutorBinding` for the project's `projectType` at read time. Returned only on single-project endpoints (`GET /api/projects/{id}`, `GET /api/projects/by-slug/{slug}`, `POST /api/projects`, `PATCH /api/projects/{id}`). Always `null` on list responses (`GET /api/projects`). |
  ```
- Append a changelog row at the bottom (today's date 2026-05-19, IMP-001):
  ```
  | 2026-05-19 | IMP-001 | `ProjectDto` gained read-only `boundExecutorProtocol` (`'devhub' \| 'orchestrator' \| null`). Populated on single-project loads via `IExecutorRouter`; always `null` on list responses by design. |
  ```

### Step 6: Update `docs/data-model.md`
**File:** `docs/data-model.md`
**Action:** Modify

- In the § Project section, add a short note (does not introduce a new entity field):
  > **Bound executor protocol** is not stored on `Project`; it is projected at read time on single-project DTO loads from the active `ExecutorBinding` for the project's `projectType`. List responses omit the projection by design.
- Append a changelog row at the bottom matching the api-spec format.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `src/DevHub.Modules.Workspace/DTOs/ProjectDtos.cs` | Modify | Add `BoundExecutorProtocol` to `ProjectDto`. |
| `src/DevHub.Modules.Workspace/Services/ProjectService.cs` | Modify | Resolve via `IExecutorRouter` in `LoadAsync`; pass `null` in `ListAsync`. |
| `tests/DevHub.Modules.Workspace.Tests/EndpointsTests.cs` | Modify | Three new integration tests covering single-load / unbound / list. |
| `docs/api-spec.md` | Modify | New `boundExecutorProtocol` field row in ProjectDto; changelog row. |
| `docs/data-model.md` | Modify | Prose note on projection-at-read; changelog row. |

## Edge Cases & Risks

- **Ctor signature churn.** `ProjectDto` is a positional record. Adding a trailing field is the lowest-blast-radius shape, but every test that constructs `ProjectDto` directly (or uses `with`) needs the new arg. Search for `new ProjectDto(` across the solution before merging — if there are widespread usages, consider switching to an init-only property instead. (Confirm during implementation: the projections live inside `ProjectService`, but tests may use `with` expressions in fixture builders.)
- **`UpdateAsync` divergence.** If `UpdateAsync` builds a `ProjectDto` inline rather than calling `LoadAsync`, that path will return a half-filled DTO. Verify both paths route through `LoadAsync`; if not, fix that as part of this task.
- **Router throws.** `IExecutorRouter.ResolveAsync` queries `ExecutorRegistryDbContext`. A transient failure would now surface on every single-project load. Acceptable: the same failure mode already exists for Create/Update (both call `IsProjectTypeBoundAsync`). Do not add resilience here — that belongs in a cross-cutting concern, not in this DTO load.
- **N+1 temptation.** Do not "fix" the list path by batching the router calls. List does not need this field. The IMP explicitly scopes it out.
- **Caching.** Do not introduce a per-request cache for the router lookup. One extra round-trip per single-project load is acceptable; premature optimization here would add complexity for no measurable win.

## Acceptance Verification

- [ ] AC: `ProjectDto` has new `BoundExecutorProtocol` field → grep the record definition.
- [ ] AC: `LoadAsync` resolves via router → read the diff.
- [ ] AC: `ListAsync` passes `null` → read the diff.
- [ ] AC: Three new tests added and green → run `dotnet test`.
- [ ] AC: All pre-existing Workspace tests pass (ctor signature update is mechanical) → `dotnet test`.
- [ ] AC: `docs/api-spec.md` updated with field row + changelog → grep the file.
- [ ] AC: `docs/data-model.md` updated with prose note + changelog → grep the file.
- [ ] AC: No new audit entries written for this read-only projection → grep `audit.` in the diff (should be unchanged).
