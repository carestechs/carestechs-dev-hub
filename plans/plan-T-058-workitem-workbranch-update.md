# Implementation Plan: T-058 — WorkItem WorkBranch on Start + new Update endpoint

## Task Reference
- **Task ID:** T-058 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-2 (WorkItem half), AC-9, AC-10 (WorkItem half). Opens an update path for WorkItem that didn't exist before.

## Overview
Two changes in this task: extend `StartWorkItemRequest` with optional `WorkBranch` (validated on start), and introduce a small `PATCH /api/projects/{pid}/work-items/{wid}` endpoint scoped to one field in v1. New `workitem:update` authorization key, operator-only for v1.

## Implementation Steps

### Step 1: Extend `StartWorkItemRequest` and DTOs
**File:** `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs` · Modify

```csharp
public sealed class StartWorkItemRequest
{
    [Required, MaxLength(255)]
    public string Title { get; init; } = string.Empty;

    public JsonElement Input { get; init; }

    [MaxLength(200)]
    public string? WorkBranch { get; init; }
}

public sealed record UpdateWorkItemRequest
{
    [MaxLength(200)]
    public string? WorkBranch { get; init; }
}
```

Extend the projection DTOs (`WorkItemSummaryDto` + `WorkItemDto`) with a `string? WorkBranch` field placed after `CreatedBy`.

### Step 2: Add `WorkBranch` to the entity write in `StartAsync`
**File:** `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` · Modify

Inside `StartAsync`, before the executor call:

```csharp
if (request.WorkBranch is not null)
    CodeSourceValidator.ValidateBranch(request.WorkBranch);
```

When the `WorkItem` entity is constructed, set `WorkBranch = request.WorkBranch`. Update the projection helper that returns `WorkItemDto` to include the new field.

### Step 3: Add `UpdateAsync` to the service interface
**File:** `src/DevHub.Modules.WorkItems/Services/IWorkItemsService.cs` · Modify

```csharp
Task<WorkItemDto> UpdateAsync(
    Guid projectId, Guid workItemId, UpdateWorkItemRequest request, Guid actingMemberId, CancellationToken ct);
```

### Step 4: Implement `UpdateAsync`
**File:** `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` · Modify

```csharp
public async Task<WorkItemDto> UpdateAsync(
    Guid projectId, Guid workItemId, UpdateWorkItemRequest request, Guid actingMemberId, CancellationToken ct)
{
    var project = await projects.FindByIdAsync(projectId, ct)
        ?? throw new NotFoundException("Project not found.");

    await authz.EnsureOperatorAsync(actingMemberId, "workitem:update", ct);

    if (request.WorkBranch is not null && request.WorkBranch.Length > 0)
        CodeSourceValidator.ValidateBranch(request.WorkBranch);

    await using var tx = await db.Database.BeginTransactionAsync(ct);

    var wi = await db.WorkItems.FirstOrDefaultAsync(w => w.Id == workItemId && w.ProjectId == projectId, ct)
        ?? throw new NotFoundException("Work item not found.");

    var before = wi.WorkBranch;
    // Treat empty string as "clear the override"; null means "leave unchanged".
    wi.WorkBranch = request.WorkBranch switch
    {
        null => wi.WorkBranch,
        "" => null,
        _ => request.WorkBranch,
    };

    var details = new Dictionary<string, object?>();
    if (before != wi.WorkBranch)
    {
        details["workBranchBefore"] = before;
        details["workBranchAfter"] = wi.WorkBranch;
    }

    if (details.Count > 0)
    {
        await audit.WriteAsync(new AuditWriteRequest("WorkItem", workItemId, "workitem:update", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            ProjectId = projectId,
            Details = details,
        }, ct);
    }

    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return await LoadDtoAsync(wi.Id, ct);
}
```

The empty-string-means-clear convention matches what T-062 (UI) will send from the inline edit form.

### Step 5: Register the new authorization key
**File:** `src/DevHub.Modules.Workspace/Services/ProjectAuthorizationService.cs` (or wherever the keys are enumerated) · Modify

Add `"workitem:update"` to the list of known action keys. v1 = operator-only — `EnsureOperatorAsync` already handles that without needing a per-role contract.

### Step 6: Add the controller endpoint
**File:** `src/DevHub.Modules.WorkItems/Controllers/WorkItemsController.cs` · Modify

```csharp
[HttpPatch("{workItemId:guid}")]
public async Task<IActionResult> Update(
    Guid projectId, Guid workItemId, [FromBody] UpdateWorkItemRequest req, CancellationToken ct) =>
    Ok(new EnvelopeDto<WorkItemDto>(await svc.UpdateAsync(projectId, workItemId, req, me.MemberId, ct)));
```

### Step 7: Update `docs/api-spec.md`
**File:** `docs/api-spec.md` · Modify

Document `PATCH /api/projects/{pid}/work-items/{wid}` with request body + response shape. Add a changelog row:

```
| 2026-05-17 | FEAT-008 | WorkItem DTO + StartWorkItemRequest gained workBranch. New PATCH endpoint scoped to workBranch only in v1. |
```

### Step 8: Run the suite
**Bash:**

```bash
dotnet test
```

## Files Affected
| File | Action |
|------|--------|
| `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/IWorkItemsService.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Controllers/WorkItemsController.cs` | Modify |
| `src/DevHub.Modules.Workspace/Services/ProjectAuthorizationService.cs` | Modify |
| `docs/api-spec.md` | Modify |

## Edge Cases & Risks
- **Empty-string-means-clear vs. null-means-unchanged.** The Workspace Update path uses null = unchanged; the WorkItem Update path adopts the same convention but adds empty-string = clear. Documented in the controller doc-comment so future maintainers don't re-invent the contract.
- **`workitem:update` authorization key.** v1 = operator-only. If a future FEAT wants member-with-role editing, the authz contract gains an entry — no migration needed since these keys are just strings.
- **PATCH against an unknown work item ID returns 404, not 403** — same as the existing Workspace PATCHes. Auth check happens after the project lookup but before the work item lookup; the operator check fires on every PATCH, even no-op ones.

## Acceptance Verification
- [ ] `POST /api/projects/{pid}/work-items` with `workBranch` set persists it.
- [ ] Invalid `workBranch` on start returns 400 with `ValidationException` problem detail, no work item row created.
- [ ] `PATCH /api/projects/{pid}/work-items/{wid}` accepts `{ workBranch: "feat/x" }` and reflects the change in the response.
- [ ] Empty-string PATCH clears the override; the response shows `workBranch: null`.
- [ ] Audit row written for every observed change.
