# Implementation Plan: T-059 — Forward `intake.codeSource` on Start

## Task Reference
- **Task ID:** T-059 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-5, AC-6, AC-7. The whole point of the FEAT — the actual wire change.

## Overview
Add a `CodeSourcePayload` contract record. Thread it through `ExecutorHttpClient.StartAsync` so the body shape is `{ input, correlationMarker }` when no repo is set on the project (byte-for-byte unchanged), and `{ input, correlationMarker, intake: { codeSource: {...} } }` when it is. Log INFO when omitting so we can grep for "callers still on the old contract".

## Implementation Steps

### Step 1: Create the payload record
**File:** `src/DevHub.Contracts/Executors/CodeSourcePayload.cs` · Create

```csharp
using System.Text.Json.Serialization;

namespace DevHub.Contracts.Executors;

public sealed record CodeSourcePayload(
    [property: JsonPropertyName("repo")] string Repo,
    [property: JsonPropertyName("baseBranch")] string BaseBranch,
    [property: JsonPropertyName("workBranch"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? WorkBranch);
```

The `WhenWritingNull` attribute is what makes `workBranch` disappear from the JSON when null (AC-7).

### Step 2: Extend the executor client interface
**File:** `src/DevHub.Modules.WorkItems/Services/IExecutorHttpClient.cs` · Modify

```csharp
Task<ExecutorStartResponse> StartAsync(
    ExecutorRegistrationDescriptor executor,
    string correlationMarker,
    JsonElement input,
    CodeSourcePayload? codeSource,
    CancellationToken ct = default);
```

### Step 3: Update the client implementation
**File:** `src/DevHub.Modules.WorkItems/Services/ExecutorHttpClient.cs` · Modify

```csharp
public async Task<ExecutorStartResponse> StartAsync(
    ExecutorRegistrationDescriptor executor, string correlationMarker, JsonElement input,
    CodeSourcePayload? codeSource, CancellationToken ct = default)
{
    using var req = NewRequest(HttpMethod.Post, executor, correlationMarker, "/work-items");
    req.Content = codeSource is null
        ? JsonContent.Create(new { input, correlationMarker })
        : JsonContent.Create(new { input, correlationMarker, intake = new { codeSource } });
    return await SendJsonAsync<ExecutorStartResponse>(executor, req, ct);
}
```

The null branch must keep producing the exact previous bytes — that's what AC-6 verifies and what keeps the existing FakeExecutor tests green.

### Step 4: Build the payload in `WorkItemsService.StartAsync`
**File:** `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` · Modify

Inside `StartAsync`, after resolving the project + descriptor and before the executor call:

```csharp
CodeSourcePayload? codeSource = null;
if (!string.IsNullOrEmpty(project.Repo) && !string.IsNullOrEmpty(project.DefaultBranch))
{
    codeSource = new CodeSourcePayload(
        Repo: project.Repo,
        BaseBranch: project.DefaultBranch,
        WorkBranch: request.WorkBranch);
}
else
{
    log.LogInformation(
        "codeSourceMissing=true projectId={ProjectId} workItemId-pending=true — orchestrator deprecation timer applies",
        projectId);
}
```

Pass `codeSource` to the existing `executorClient.StartAsync` call:

```csharp
startResp = await executorClient.StartAsync(descriptor, marker, request.Input, codeSource, ct);
```

The "both set" gate prevents sending half-payloads (a `codeSource` block missing `baseBranch` would be invalid on the orchestrator side; better to omit entirely with a log line).

### Step 5: Update the test doubles
**File:** `src/DevHub.Modules.WorkItems/Services/IExecutorHttpClient.cs` consumers in `tests/` · Modify

Any test double implementing `IExecutorHttpClient` needs the new parameter. Most likely in `tests/DevHub.Modules.WorkItems.Tests/Acceptance/FakeExecutorHttpClient.cs` (or similar). Default the parameter so existing call-sites in tests stay compiling — but only at the test-double level, NOT in the production interface.

### Step 6: Run the suite
**Bash:**

```bash
dotnet test
```

Existing tests pass — the no-codeSource branch is byte-for-byte unchanged.

## Files Affected
| File | Action |
|------|--------|
| `src/DevHub.Contracts/Executors/CodeSourcePayload.cs` | Create |
| `src/DevHub.Modules.WorkItems/Services/IExecutorHttpClient.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/ExecutorHttpClient.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` | Modify |
| Test doubles in `tests/DevHub.Modules.WorkItems.Tests/` | Modify (signature only) |

## Edge Cases & Risks
- **Half-set coordinates** (only `repo`, no `defaultBranch`, or vice versa). The Step-4 gate omits `codeSource` entirely in that case. The UI banner from T-061 prompts the operator to set both. The brief acknowledges this; we do not invent a `null` baseBranch to send.
- **Orchestrator strict-flag flip.** Once `LIFECYCLE_CODE_SOURCE_REQUIRED=true`, the no-`codeSource` branch becomes a 502 from DevHub's perspective (executor 400 → `ExecutorFailureException` → 502 problem detail). The existing failure path already audits + bubbles; nothing to change here.
- **`intake` envelope vs. orchestrator's full shape.** The orchestrator spec puts `workItem` AND `codeSource` under `intake`. We only put `codeSource` there because `input` already encodes the work item context for the existing FakeExecutor contract. If a later FEAT promotes work-item identity into the same envelope, this is the natural seam.

## Acceptance Verification
- [ ] Project with repo+branch set → start payload contains `intake.codeSource` byte-for-byte as specified.
- [ ] Project with no repo → start payload contains no `intake` key (verified via JsonNode).
- [ ] WorkBranch null → `intake.codeSource` has no `workBranch` field.
- [ ] INFO log emitted on every codeSource-less start.
- [ ] `dotnet test` is green.
