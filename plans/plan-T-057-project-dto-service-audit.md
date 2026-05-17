# Implementation Plan: T-057 — Project DTOs + service + audit for Repo / DefaultBranch

## Task Reference
- **Task ID:** T-057 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-2, AC-3, AC-10 (Project half). Makes `Project` the source of truth for repo coordinates.

## Overview
Thread two new optional fields through `ProjectDto`, `CreateProjectRequest`, `UpdateProjectRequest`, the service layer, the projection helper, and the audit details. Validation calls in `CreateAsync` + `UpdateAsync` happen *before* any DB write. No controller changes (controllers stay thin parse-and-forward).

## Implementation Steps

### Step 1: Extend DTOs
**File:** `src/DevHub.Modules.Workspace/DTOs/ProjectDtos.cs` · Modify

`ProjectDto` (record) gains:

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
    DateTimeOffset CreatedAt);
```

`CreateProjectRequest` gains `string? Repo`, `string? DefaultBranch` after `Description`. `UpdateProjectRequest` same.

### Step 2: Update the projection
**File:** `src/DevHub.Modules.Workspace/Services/ProjectService.cs` · Modify

In `LoadAsync` (or whichever helper maps `Project` → `ProjectDto`), include `p.Repo` and `p.DefaultBranch` in the `Select` projection. Place them between `Description` and the in-flight count.

### Step 3: Validate + persist in `CreateAsync`
**File:** `src/DevHub.Modules.Workspace/Services/ProjectService.cs` · Modify

Inside `CreateAsync`, after the operator auth check and before the transaction:

```csharp
if (req.Repo is not null) CodeSourceValidator.ValidateRepo(req.Repo);
if (req.DefaultBranch is not null) CodeSourceValidator.ValidateBranch(req.DefaultBranch);
```

Set the new properties on the `Project` literal:

```csharp
var project = new Project
{
    Name = req.Name,
    Slug = req.Slug,
    ProjectType = req.ProjectType,
    OwningTeamId = req.OwningTeamId,
    Description = req.Description,
    Repo = req.Repo,
    DefaultBranch = req.DefaultBranch,
};
```

Add to the audit details when set:

```csharp
Details = new Dictionary<string, object?>
{
    ["slug"] = req.Slug,
    ["projectType"] = req.ProjectType,
    ["repo"] = req.Repo,
    ["defaultBranch"] = req.DefaultBranch,
},
```

### Step 4: Validate + persist in `UpdateAsync`
**File:** `src/DevHub.Modules.Workspace/Services/ProjectService.cs` · Modify

Mirror the same validation calls. Capture before/after for audit:

```csharp
var repoBefore = project.Repo;
var defaultBranchBefore = project.DefaultBranch;

if (req.Repo is not null)
{
    CodeSourceValidator.ValidateRepo(req.Repo);
    project.Repo = req.Repo;
}
if (req.DefaultBranch is not null)
{
    CodeSourceValidator.ValidateBranch(req.DefaultBranch);
    project.DefaultBranch = req.DefaultBranch;
}
```

Add only the changed fields to the audit details:

```csharp
var details = new Dictionary<string, object?>();
if (repoBefore != project.Repo)
{
    details["repoBefore"] = repoBefore;
    details["repoAfter"] = project.Repo;
}
if (defaultBranchBefore != project.DefaultBranch)
{
    details["defaultBranchBefore"] = defaultBranchBefore;
    details["defaultBranchAfter"] = project.DefaultBranch;
}
// …merge with any existing details the method already builds…
```

Note: `null`-out is not supported via PATCH in v1. A `null` in the request body means "leave unchanged" (matches the existing Update semantics — only set fields are applied).

### Step 5: Update `docs/api-spec.md`
**File:** `docs/api-spec.md` · Modify

Project section: extend the `POST /api/projects`, `PATCH /api/projects/{id}`, and `GET` envelopes with the two new fields. Note validation rules inline. Add changelog row:

```
| 2026-05-17 | FEAT-008 | Project DTO gained repo, defaultBranch (optional). Boundary validation matches orchestrator intake.codeSource schema. |
```

### Step 6: Run the suite
**Bash:**

```bash
dotnet test
```

Existing tests should pass — DTO additions are nullable and backward-compatible.

## Files Affected
| File | Action |
|------|--------|
| `src/DevHub.Modules.Workspace/DTOs/ProjectDtos.cs` | Modify |
| `src/DevHub.Modules.Workspace/Services/ProjectService.cs` | Modify |
| `docs/api-spec.md` | Modify |

## Edge Cases & Risks
- **`UpdateProjectRequest` null = "leave unchanged" is the existing convention.** This means there's no way to clear `repo` once set via the PATCH path. v1 acceptable — operators can edit, not clear. If clearing is later needed, introduce a discriminated explicit-null marker (`JsonElement` or a wrapper).
- **`ProjectDto` is a positional record.** Every consumer that destructures positionally will need to pick up the two new fields. Search for `new ProjectDto(` in `src/` and `tests/` and adjust.
- **`InFlightWorkItems` field placement.** If the projection already returns `InFlightWorkItems`, keep it after the new optional fields so consumers reading by name still work.

## Acceptance Verification
- [ ] `POST /api/projects` round-trips `repo` and `defaultBranch`.
- [ ] `PATCH /api/projects/{id}` updates them with audit details containing before/after.
- [ ] `GET` endpoints surface them.
- [ ] `dotnet test` is green.
- [ ] `docs/api-spec.md` updated + changelog.
