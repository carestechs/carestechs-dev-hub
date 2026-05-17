# Implementation Plan: T-084 — EF migrations for `WorkItem.ExecutorRunId` + `ExecutorRegistration.Protocol`

## Task Reference
- **Task ID:** T-084 · **Type:** Database · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-1, AC-2, AC-9. Foundation; everything else reads from these columns.

## Overview
Two columns across two modules. Standard FEAT-008/009 migration shape: nullable / defaulted, default values on the cross-module descriptor record so positional callers keep compiling, EF `HasDefaultValue` for the boolean-flag equivalent.

## Implementation Steps

### Step 1: Extend `WorkItem`
**File:** `src/DevHub.Modules.WorkItems/Entities/WorkItem.cs` · Modify

```csharp
public Guid? ExecutorRunId { get; set; }
```

Add after `CurrentTaskId`. Nullable uuid.

### Step 2: Extend `ExecutorRegistration`
**File:** `src/DevHub.Modules.ExecutorRegistry/Entities/ExecutorRegistration.cs` · Modify

```csharp
public string Protocol { get; set; } = "devhub";
```

Property-init default (`"devhub"`) so existing-row defaults work even before EF's `HasDefaultValue` lands.

### Step 3: DbContext property configs
**File:** `src/DevHub.Modules.WorkItems/WorkItemsDbContext.cs` · Modify

Inside the `Entity<WorkItem>` block:

```csharp
e.Property(x => x.ExecutorRunId);  // uuid?, default null, no extra config needed
```

**File:** `src/DevHub.Modules.ExecutorRegistry/ExecutorRegistryDbContext.cs` · Modify

Inside the `Entity<ExecutorRegistration>` block:

```csharp
e.Property(x => x.Protocol).HasMaxLength(20).HasDefaultValue("devhub");
```

### Step 4: Cross-module descriptor
**File:** `src/DevHub.Contracts/Executors/ExecutorRegistrationDescriptor.cs` · Modify

Add `Protocol` with a default so positional consumers compile unchanged:

```csharp
public sealed record ExecutorRegistrationDescriptor(
    Guid Id,
    string Key,
    string DisplayName,
    string BaseUrl,
    ExecutorStatus Status,
    IReadOnlyList<CheckpointContractDescriptor> Contracts,
    string Protocol = "devhub");
```

### Step 5: Router projects the field
**File:** `src/DevHub.Modules.ExecutorRegistry/Services/ExecutorRouter.cs` · Modify

In the `Map(ExecutorRegistration)` helper:

```csharp
internal static ExecutorRegistrationDescriptor Map(ExecutorRegistration e) =>
    new(e.Id, e.Key, e.DisplayName, e.BaseUrl, e.Status,
        e.CheckpointContracts.Select(Map).ToList(),
        e.Protocol);
```

### Step 6: Generate migrations
**Bash:**

```bash
dotnet ef migrations add AddWorkItemExecutorRunId \
  --project src/DevHub.Modules.WorkItems \
  --startup-project src/DevHub.Api \
  --context WorkItemsDbContext

dotnet ef migrations add AddExecutorRegistrationProtocol \
  --project src/DevHub.Modules.ExecutorRegistry \
  --startup-project src/DevHub.Api \
  --context ExecutorRegistryDbContext
```

Inspect each — should produce only one `AddColumn` op.

### Step 7: Update `docs/data-model.md`
**File:** `docs/data-model.md` · Modify

Add field rows to the WorkItem and ExecutorRegistration tables. Changelog row:

```
| 2026-05-17 (FEAT-010 / T-084) | WorkItem gained nullable `executor_run_id` (uuid). ExecutorRegistration gained `protocol` (varchar 20, default `'devhub'`). Drives the `IExecutorHttpClient` implementation selection (FEAT-010). |
```

### Step 8: Run the suite
**Bash:**

```bash
dotnet test
```

190/190 still green. Existing fixtures backfill defaults — no breakage.

## Files Affected
| File | Action |
|---|---|
| `src/DevHub.Modules.WorkItems/Entities/WorkItem.cs` | Modify |
| `src/DevHub.Modules.WorkItems/WorkItemsDbContext.cs` | Modify |
| `src/DevHub.Modules.ExecutorRegistry/Entities/ExecutorRegistration.cs` | Modify |
| `src/DevHub.Modules.ExecutorRegistry/ExecutorRegistryDbContext.cs` | Modify |
| `src/DevHub.Contracts/Executors/ExecutorRegistrationDescriptor.cs` | Modify |
| `src/DevHub.Modules.ExecutorRegistry/Services/ExecutorRouter.cs` | Modify |
| 2 migration files | Create |
| `docs/data-model.md` | Modify |

## Edge Cases & Risks
- **Positional `ExecutorRegistrationDescriptor` callers.** With a default on the new last parameter, existing positional construction still compiles. Search for `new ExecutorRegistrationDescriptor(` to confirm.
- **`HasDefaultValue` + Postgres backfill.** EF Core emits the DEFAULT at column level so existing rows get `'devhub'` on the ALTER. Verified by `dotnet test` running migrations cleanly.

## Acceptance Verification
- [ ] Both columns present; defaults applied to existing rows.
- [ ] `dotnet test` passes.
- [ ] `docs/data-model.md` updated + changelog row.
