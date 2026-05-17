# Implementation Plan: T-068 — Signal endpoint forwards `taskId` + validates `payload.assignee` non-empty

## Task Reference
- **Task ID:** T-068 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-4, AC-5, AC-10. The operator-facing path: signal → DevHub validates + forwards → executor's idempotency hash takes over.

## Overview
`SignalRequest` gains optional `taskId`. `CheckpointSignalsService.SignalAsync` forwards it. Boundary validation: when the active contract has `perTask=true` AND `checkpointKey="assignment-confirmed"`, `payload.assignee` must be a non-empty string. Audit details carry `taskId` and `assignee`.

## Implementation Steps

### Step 1: Extend `SignalRequest`
**File:** `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs` · Modify

```csharp
public sealed class SignalRequest
{
    [Required, MaxLength(60)]
    public string Outcome { get; init; } = string.Empty;

    public JsonElement? Payload { get; init; }

    [MaxLength(60)]
    public string? TaskId { get; init; }
}
```

### Step 2: Extend `IExecutorHttpClient.SignalAsync` signature
**File:** `src/DevHub.Contracts/Executors/IExecutorHttpClient.cs` · Modify

```csharp
Task<ExecutorSignalResponse> SignalAsync(
    ExecutorRegistrationDescriptor executor,
    string correlationMarker,
    string checkpointKey,
    string outcome,
    JsonElement? payload,
    string? taskId,
    CancellationToken cancellationToken = default);
```

### Step 3: Update the `ExecutorHttpClient` implementation
**File:** `src/DevHub.Modules.WorkItems/Services/ExecutorHttpClient.cs` · Modify

```csharp
public async Task<ExecutorSignalResponse> SignalAsync(
    ExecutorRegistrationDescriptor executor, string correlationMarker, string checkpointKey,
    string outcome, JsonElement? payload, string? taskId, CancellationToken ct = default)
{
    using var req = NewRequest(HttpMethod.Post, executor, correlationMarker,
        $"/work-items/{correlationMarker}/checkpoints/{Uri.EscapeDataString(checkpointKey)}/signal");
    // Omit (don't send null) when no taskId — matches the orchestrator's omit-don't-null pattern.
    req.Content = taskId is null
        ? JsonContent.Create(new { outcome, payload })
        : JsonContent.Create(new { outcome, payload, taskId });
    return await SendJsonAsync<ExecutorSignalResponse>(executor, req, ct);
}
```

### Step 4: Validate + forward in `CheckpointSignalsService`
**File:** `src/DevHub.Modules.WorkItems/Services/CheckpointSignalsService.cs` · Modify

Before the executor call (after the existing authz step):

```csharp
// Boundary validation for the assignment-confirmed per-task checkpoint.
if (contract.PerTask && checkpointKey == "assignment-confirmed")
{
    var assignee = ExtractAssignee(request.Payload);
    if (string.IsNullOrWhiteSpace(assignee))
        throw new ValidationException(new Dictionary<string, string[]>
        {
            ["payload.assignee"] = new[] { "payload.assignee must be a non-empty string for assignment-confirmed signals" },
        });
}
```

Where `ExtractAssignee` is a small helper:

```csharp
private static string? ExtractAssignee(JsonElement? payload)
{
    if (payload is null || payload.Value.ValueKind != JsonValueKind.Object) return null;
    if (!payload.Value.TryGetProperty("assignee", out var a)) return null;
    return a.ValueKind == JsonValueKind.String ? a.GetString() : null;
}
```

Forward to executor with `request.TaskId`:

```csharp
signalResp = await executorClient.SignalAsync(
    descriptor, wi.ExecutorCorrelationMarker,
    checkpointKey, request.Outcome, request.Payload,
    request.TaskId, ct);
```

### Step 5: Audit details
**File:** `src/DevHub.Modules.WorkItems/Services/CheckpointSignalsService.cs` · Modify

The existing `workitem:signal` audit write gets two extra keys when present:

```csharp
Details = new Dictionary<string, object?>
{
    ["checkpointKey"] = checkpointKey,
    ["outcome"] = request.Outcome,
    ["taskId"] = request.TaskId,
    ["assignee"] = ExtractAssignee(request.Payload),
},
```

The `ExtractAssignee` is reusable here — returns `null` when the payload doesn't carry one, which is fine; the audit row simply omits it on serialize (JSONB null).

### Step 6: Update `docs/api-spec.md`
**File:** `docs/api-spec.md` · Modify

The `POST /api/projects/{pid}/work-items/{wid}/checkpoints/{key}/signal` request body example gains `taskId`. Note the assignment-confirmed assignee guard inline. Changelog:

```
| 2026-05-17 (FEAT-009 / T-068) | SignalRequest gained optional taskId, forwarded to the executor verbatim. assignment-confirmed contracts (perTask=true + checkpointKey="assignment-confirmed") require non-empty payload.assignee at the DevHub boundary; rejected values produce 400 with no executor call. Audit details carry taskId + assignee. |
```

### Step 7: Run the suite
**Bash:**

```bash
dotnet test
```

182/182 still green. The forward shape with no `taskId` (the old shape) is byte-for-byte unchanged.

## Files Affected
| File | Action |
|------|--------|
| `src/DevHub.Modules.WorkItems/DTOs/WorkItemDtos.cs` | Modify (SignalRequest) |
| `src/DevHub.Contracts/Executors/IExecutorHttpClient.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/ExecutorHttpClient.cs` | Modify |
| `src/DevHub.Modules.WorkItems/Services/CheckpointSignalsService.cs` | Modify |
| `docs/api-spec.md` | Modify |

## Edge Cases & Risks
- **`assignment-confirmed` checkpoint key collision.** Hard-coding the string in the service is OK for v1 (the brief explicitly names it). When a second per-task contract is added, this guard generalizes to "all per-task contracts require some payload key." For now, keep the assignee check scoped to the specific key.
- **Whitespace-only `assignee`.** `IsNullOrWhiteSpace` catches it — rejected.
- **Payload with `assignee` as a non-string** (number, object). `TryGetProperty` returns the element; we check `ValueKind == String`. Anything else is treated as missing → 400.
- **No DevHub-side dedupe.** The executor's `(run_id, "assignment-confirmed", task_id)` hash handles idempotency. Don't add a DevHub layer.

## Acceptance Verification
- [ ] SignalRequest carries `taskId`.
- [ ] Executor receives `taskId` in the body when set; absent (not null) otherwise.
- [ ] Empty/missing assignee on assignment-confirmed → 400 with `payload.assignee` error key.
- [ ] Audit row includes `taskId` + `assignee` (when present).
- [ ] `dotnet test` is green.
