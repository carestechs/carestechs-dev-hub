# Implementation Plan: T-066 — `CheckpointContract.PerTask` flag on registration + DTO

## Task Reference
- **Task ID:** T-066 · **Type:** Backend · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-1. The contract registry has to surface the new flag for the reconciler to key per-task.

## Overview
A small additive change to the executor-contract registration shape: `perTask: bool` (optional, default `false`) on the request, the DTO, and the cross-module descriptor consumed by the reconciler.

## Implementation Steps

### Step 1: Extend the request DTO
**File:** `src/DevHub.Modules.ExecutorRegistry/DTOs/*.cs` (whichever holds `ReplaceContractsRequest` — search for it) · Modify

Inside `ReplaceContractsRequest.CheckpointContractInput`, add:

```csharp
public bool PerTask { get; init; }
```

Default `false`. No `[Required]`.

### Step 2: Extend the response DTO
**File:** same module DTOs · Modify

`CheckpointContractDto` gains `bool PerTask` at the end of the positional record. Update every positional construction site.

### Step 3: Persist on the entity
**File:** `src/DevHub.Modules.ExecutorRegistry/Services/*.cs` (the contract-replace service) · Modify

When materializing `CheckpointContract` entities from the request, set `PerTask = input.PerTask`. The column already exists from T-064.

### Step 4: Extend the cross-module descriptor
**File:** `src/DevHub.Contracts/Executors/*.cs` — find the record returned by `IExecutorRouter.GetCheckpointContractAsync` (and the broader `ExecutorRegistrationDescriptor.Contracts` collection) · Modify

Add `bool PerTask` to the descriptor record. Default to `false` if positional.

### Step 5: Update the router's projection
**File:** `src/DevHub.Modules.ExecutorRegistry/Services/ExecutorRouter.cs` · Modify

When mapping `CheckpointContract` to the descriptor, include `c.PerTask`. The reconciler reads it from here in T-067.

### Step 6: Update `docs/api-spec.md`
**File:** `docs/api-spec.md` · Modify

The `POST /api/admin/executors/{id}/checkpoint-contracts` request body example gains a `perTask` field. Same for the GET response. Changelog:

```
| 2026-05-17 (FEAT-009 / T-066) | CheckpointContract registration request + response gained optional perTask boolean (default false). Surfaced on IExecutorRouter descriptors so the reconciler can key pending actions per task. |
```

### Step 7: Run the suite
**Bash:**

```bash
dotnet test
```

182/182 still green. The contract-tests in `DevHub.Modules.ExecutorRegistry.Tests` should keep passing — all new fields default.

## Files Affected
| File | Action |
|------|--------|
| `src/DevHub.Modules.ExecutorRegistry/DTOs/*.cs` | Modify (request + DTO) |
| `src/DevHub.Modules.ExecutorRegistry/Services/*.cs` | Modify (persist + project) |
| `src/DevHub.Contracts/Executors/*.cs` | Modify (descriptor) |
| `docs/api-spec.md` | Modify |

## Edge Cases & Risks
- **Replace semantics on contract list.** `POST .../checkpoint-contracts` atomically replaces the whole set per FEAT-003's design. Operators must include `perTask=true` on every relevant contract in the replacement payload — there's no per-key PATCH. This is the contract; document it in the api-spec example.
- **Positional `CheckpointContractDto` callers.** Same search-and-update drill as previous tasks. The compiler catches misses.
- **No validation guards on `perTask`.** It's an opt-in flag; the executor decides whether to use task ids. DevHub trusts the executor's behavior.

## Acceptance Verification
- [ ] Request body accepts `perTask`; response carries it.
- [ ] GET `/api/admin/executors/{id}` round-trips the flag.
- [ ] Router descriptor exposes `perTask`.
- [ ] `dotnet test` is green.
