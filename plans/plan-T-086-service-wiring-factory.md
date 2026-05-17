# Implementation Plan: T-086 — Wire `ExecutorRunId` + factory through `WorkItemsService`

## Task Reference
- **Task ID:** T-086 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-2, AC-9. Without this, T-085's client is unreachable from the service layer.

## Overview
Two coupled changes: (1) change `IExecutorHttpClient`'s method signatures to take a `WorkItemRef` (rather than bare `correlationMarker`) so both implementations can route by the right id; (2) add an `IExecutorClientFactory` that resolves the right implementation per executor descriptor and use it in `WorkItemsService` + `CheckpointSignalsService` + the stream forwarder.

Persist `WorkItem.ExecutorRunId` on Start so subsequent operations have the id available.

## Implementation Steps

### Step 1: Define `WorkItemRef`
**File:** `src/DevHub.Contracts/Executors/WorkItemRef.cs` · Create

```csharp
namespace DevHub.Contracts.Executors;

/// <summary>
/// Identifies a work item to <see cref="IExecutorHttpClient"/> implementations.
/// Marker is DevHub's stable id (forever); ExecutorRunId is the orchestrator's
/// run uuid, populated after Start succeeds (null beforehand).
/// </summary>
public sealed record WorkItemRef(string Marker, Guid? ExecutorRunId);
```

### Step 2: Update `IExecutorHttpClient` signatures
**File:** `src/DevHub.Contracts/Executors/IExecutorHttpClient.cs` · Modify

Replace every `string correlationMarker` parameter with `WorkItemRef workItem`. Five methods total.

```csharp
public interface IExecutorHttpClient
{
    Task<ExecutorStartResponse> StartAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        JsonElement input,
        CodeSourcePayload? codeSource,
        CancellationToken cancellationToken = default);

    Task<ExecutorFetchResponse> FetchStateAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        CancellationToken cancellationToken = default);

    Task<ExecutorSignalResponse> SignalAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        string checkpointKey,
        string outcome,
        JsonElement? payload,
        string? taskId,
        CancellationToken cancellationToken = default);

    Task<ExecutorStreamConnection> OpenStreamAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        CancellationToken cancellationToken = default);

    Task CancelAsync(
        ExecutorRegistrationDescriptor executor,
        WorkItemRef workItem,
        CancellationToken cancellationToken = default);
}
```

### Step 3: Adapt the existing `ExecutorHttpClient`
**File:** `src/DevHub.Modules.WorkItems/Services/ExecutorHttpClient.cs` · Modify

Mechanical: every method's `correlationMarker` parameter becomes `workItem`; inside, replace usages with `workItem.Marker`. URLs that embed the marker (`/work-items/{marker}/...`) unchanged.

### Step 4: Adapt `OrchestratorExecutorClient`
**File:** `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs` · Modify

- `StartAsync`: `workItem.Marker` is the DevHub-side correlation id (logged + sent in `X-DevHub-Correlation` header, used as `intake.workItem.id`).
- All other methods: use `workItem.ExecutorRunId!.Value` directly. If null, throw `InvalidOperationException` — should never happen on the post-Start path.
- The `LookupRunIdAsync` temporary helper from T-085 is now **removed** — the run id is passed in. Cleaner.

### Step 5: Create the factory
**File:** `src/DevHub.Modules.WorkItems/Services/IExecutorClientFactory.cs` · Create

```csharp
using DevHub.Contracts.Executors;

namespace DevHub.Modules.WorkItems.Services;

public interface IExecutorClientFactory
{
    IExecutorHttpClient Resolve(ExecutorRegistrationDescriptor descriptor);
}
```

**File:** `src/DevHub.Modules.WorkItems/Services/ExecutorClientFactory.cs` · Create

```csharp
using DevHub.Contracts.Executors;
using DevHub.Modules.WorkItems.Services.Orchestrator;

namespace DevHub.Modules.WorkItems.Services;

internal sealed class ExecutorClientFactory(
    ExecutorHttpClient devhubClient,
    OrchestratorExecutorClient orchestratorClient) : IExecutorClientFactory
{
    public IExecutorHttpClient Resolve(ExecutorRegistrationDescriptor descriptor) =>
        descriptor.Protocol switch
        {
            "orchestrator" => orchestratorClient,
            _ => devhubClient,  // "devhub" + any unknown value falls back to legacy
        };
}
```

### Step 6: DI registration
**File:** `src/DevHub.Modules.WorkItems/WorkItemsModuleExtensions.cs` · Modify

Register both clients (concrete types) plus the factory:

```csharp
services.AddHttpClient<ExecutorHttpClient>();
services.AddHttpClient<OrchestratorExecutorClient>();
services.AddSingleton<IExecutorClientFactory, ExecutorClientFactory>();
// Remove the bare `IExecutorHttpClient` registration if one existed — every caller now uses the factory.
```

### Step 7: Service-layer call-sites use the factory
**File:** `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` · Modify

Replace the injected `IExecutorHttpClient` with `IExecutorClientFactory`. At each call site:

```csharp
var client = factory.Resolve(descriptor);
var workItemRef = new WorkItemRef(workItem.ExecutorCorrelationMarker, workItem.ExecutorRunId);
var resp = await client.FetchStateAsync(descriptor, workItemRef, ct);
```

For Start, the `WorkItemRef.ExecutorRunId` is null going in; after the executor responds, parse `runId` from `startResp.ExecutorState` and persist on the new `WorkItem` row.

```csharp
startResp = await client.StartAsync(descriptor, new WorkItemRef(marker, null), request.Input, codeSource, ct);
var runId = TryExtractRunId(startResp.ExecutorState);  // helper, parses { "runId": "..." }
var workItem = new WorkItem
{
    // ... existing fields ...
    ExecutorRunId = runId,
};
```

Where `TryExtractRunId(JsonElement)` reads `"runId"` if present (orchestrator path) and returns null otherwise (devhub path — FakeExecutor doesn't surface a run id).

### Step 8: Mirror in `CheckpointSignalsService`
**File:** `src/DevHub.Modules.WorkItems/Services/CheckpointSignalsService.cs` · Modify

Inject the factory. At the `SignalAsync` call site, build the `WorkItemRef` from `wi.ExecutorCorrelationMarker` + `wi.ExecutorRunId` and resolve the client.

### Step 9: Stream forwarder
**File:** `src/DevHub.Modules.WorkItems/Controllers/WorkItemsController.cs` and the stream forwarder · Modify

Inject the factory in the stream-related path. Same pattern.

### Step 10: ExecutorRegistry DTO + service surface the field
**File:** `src/DevHub.Modules.ExecutorRegistry/DTOs/ExecutorDtos.cs` · Modify

Add `Protocol` to:
- `CreateExecutorRequest` (optional; default in service layer if null).
- `UpdateExecutorRequest` (optional).
- `ExecutorDto` (always emit).

**File:** `src/DevHub.Modules.ExecutorRegistry/Services/ExecutorRegistrationService.cs` · Modify

`CreateAsync` defaults `Protocol` to `"devhub"` when null. `UpdateAsync` allows changing it (operator decision). `MapDto` projects it.

### Step 11: WorkItemDto surfaces ExecutorRunId
**File:** `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs` · Modify

Add `Guid? ExecutorRunId` at the end of `WorkItemDto` + `WorkItemSummaryDto` with default `null` on the positional record.

Update every positional constructor call across the service layer.

### Step 12: Update `docs/api-spec.md`
**File:** `docs/api-spec.md` · Modify

ExecutorRegistration request/response: add `protocol`. WorkItem DTO sections: add `executorRunId`. Changelog row:

```
| 2026-05-17 (FEAT-010 / T-086) | ExecutorRegistration gained `protocol` (defaults to `"devhub"`). WorkItem DTO gained `executorRunId` (nullable uuid). IExecutorHttpClient signature switched from `correlationMarker: string` to `workItem: WorkItemRef` — both implementations updated in lockstep. New ExecutorClientFactory selects implementation by descriptor.Protocol. |
```

### Step 13: Run the suite
**Bash:**

```bash
dotnet build --nologo 2>&1 | tail
dotnet test --nologo 2>&1 | grep -E "Passed!|Failed!"
```

All 190 backend tests still green. The FakeExecutor's executor row has `protocol = "devhub"` (default), so the factory returns the existing client unchanged.

## Files Affected
| File | Action |
|---|---|
| `src/DevHub.Contracts/Executors/WorkItemRef.cs` | Create |
| `src/DevHub.Contracts/Executors/IExecutorHttpClient.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/ExecutorHttpClient.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/Orchestrator/OrchestratorExecutorClient.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/IExecutorClientFactory.cs` + impl | Create |
| `src/DevHub.Modules.WorkItems/WorkItemsModuleExtensions.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/CheckpointSignalsService.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Controllers/WorkItemsController.cs` | Modify |
| `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs` | Modify |
| `src/DevHub.Modules.ExecutorRegistry/DTOs/ExecutorDtos.cs` | Modify |
| `src/DevHub.Modules.ExecutorRegistry/Services/ExecutorRegistrationService.cs` | Modify |
| `docs/api-spec.md` | Modify |

## Edge Cases & Risks
- **Interface signature change is the largest blast radius in this FEAT.** Both implementations + every call-site (~6 files) update in lockstep. The compiler is the safety net.
- **Test doubles for `IExecutorHttpClient`.** If any test file mocks the interface, the signatures must update. Search `grep -rn "IExecutorHttpClient" tests/` to find them; existing tests use the real client against the FakeExecutor, so this may be empty.
- **`TryExtractRunId` runs on every Start.** It must tolerate the devhub-protocol case (no `runId` in the response — returns null). Document.
- **Stream forwarder needs the factory too.** If the controller injects a bare `IExecutorHttpClient` today, switch to the factory. Audit `WorkItemsController.Stream` for this.

## Acceptance Verification
- [ ] `IExecutorHttpClient` signature change applied; both implementations adapted.
- [ ] Factory resolves `orchestrator` → new client; `devhub` (default) → existing client.
- [ ] `WorkItem.ExecutorRunId` persisted on Start when the executor surfaces a `runId`.
- [ ] `WorkItemDto` surfaces `executorRunId`.
- [ ] ExecutorRegistration CRUD round-trips `protocol`.
- [ ] All 190 backend tests pass — unchanged.
