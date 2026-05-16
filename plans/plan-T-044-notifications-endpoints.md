# Implementation Plan: T-044 — GET /pending + SSE /stream

## Task Reference
- **Task ID:** T-044 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-3 (badge sync on tab reopen) needs the JSON list; AC-1 (≤2s) needs the SSE push path. Both endpoints are scoped to the authenticated caller.

## Overview
Two endpoints on `NotificationsController`:
- `GET /api/notifications/pending` — JSON list of caller's non-dismissed signals, joined to project + work item + contract for display fields.
- `GET /api/notifications/stream` — SSE. Subscribes the caller to the registry from T-043; serializes each `PendingActionEvent` as one SSE chunk.

## Implementation Steps

### Step 1: DTO
**File:** `src/DevHub.Modules.Notifications/DTOs/PendingActionDto.cs` · Create

```csharp
public sealed record PendingActionDto(
    Guid ProjectId,
    string ProjectSlug,
    Guid WorkItemId,
    string WorkItemTitle,
    string CheckpointKey,
    string CheckpointDisplayName,
    DateTimeOffset RaisedAt);
```

### Step 2: Query service
**File:** `src/DevHub.Modules.Notifications/Services/NotificationsQueryService.cs` · Create

```csharp
internal sealed class NotificationsQueryService(
    NotificationsDbContext db,
    IProjectLookup projects,
    IWorkItemLookup workItems,
    IExecutorRouter router) : INotificationsQueryService
{
    public async Task<IReadOnlyList<PendingActionDto>> ListPendingForMemberAsync(Guid memberId, CancellationToken ct)
    {
        var rows = await db.PendingActionSignals.AsNoTracking()
            .Where(p => p.MemberId == memberId && p.DismissedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        var results = new List<PendingActionDto>(rows.Count);
        foreach (var row in rows)
        {
            var project = await projects.FindByIdAsync(row.ProjectId, ct);
            var wi = await workItems.FindByIdAsync(row.WorkItemId, ct);
            if (project is null || wi is null) continue; // tolerate orphans
            var contract = await router.GetCheckpointContractAsync(wi.ExecutorId, row.CheckpointKey, ct);
            results.Add(new PendingActionDto(
                row.ProjectId, project.Slug,
                row.WorkItemId, wi.Title,
                row.CheckpointKey, contract?.DisplayName ?? row.CheckpointKey,
                row.CreatedAt));
        }
        return results;
    }
}
```

Acceptable v1 N+1; v2 batches via cross-module batched lookups.

### Step 3: Controller
**File:** `src/DevHub.Modules.Notifications/Controllers/NotificationsController.cs` · Create

```csharp
[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(
    INotificationsQueryService query,
    PendingActionStreamRegistry registry,
    ICurrentMember me) : ControllerBase
{
    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken ct) =>
        Ok(new EnvelopeDto<IReadOnlyList<PendingActionDto>>(
            await query.ListPendingForMemberAsync(me.MemberId, ct)));

    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        HttpContext.Response.StatusCode = 200;
        HttpContext.Response.Headers["Content-Type"] = "text/event-stream";
        HttpContext.Response.Headers["Cache-Control"] = "no-store";
        HttpContext.Response.Headers["X-Accel-Buffering"] = "no";
        await HttpContext.Response.StartAsync(ct);

        await using var sub = registry.Subscribe(me.MemberId, out var reader);

        try
        {
            await foreach (var ev in reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(ev);
                var bytes = Encoding.UTF8.GetBytes($"data: {json}\n\n");
                await HttpContext.Response.Body.WriteAsync(bytes, ct);
                await HttpContext.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* client disconnect */ }
    }
}
```

`Subscribe` returns the `IAsyncDisposable` cleanup from T-043's registry; `await using` ensures cleanup runs on disconnect.

### Step 4: csproj + DI
**File:** `src/DevHub.Modules.Notifications/DevHub.Modules.Notifications.csproj` · Modify
Add `FrameworkReference Include="Microsoft.AspNetCore.App"` (drop redundant Configuration/Hosting abstractions packages if pruning warns).

**File:** `src/DevHub.Modules.Notifications/NotificationsModuleExtensions.cs` · Modify
```csharp
services.AddScoped<INotificationsQueryService, NotificationsQueryService>();
```

### Step 5: Verify Program.cs picks up the controller
**File:** `src/DevHub.Api/Program.cs` · Verify
`AddApplicationPart(typeof(NotificationsDbContext).Assembly)` was added at T-024; confirm it still wires the new `NotificationsController`.

## Files Affected
| File | Action |
|------|--------|
| `Notifications/DTOs/PendingActionDto.cs` | Create |
| `Notifications/Services/NotificationsQueryService.cs` + interface | Create |
| `Notifications/Controllers/NotificationsController.cs` | Create |
| `Notifications/DevHub.Modules.Notifications.csproj` | Modify (FrameworkReference) |
| `Notifications/NotificationsModuleExtensions.cs` | Modify (DI) |

## Edge Cases & Risks
- **JWT for SSE** — the T-037 `JwtBearerEvents.OnMessageReceived` shim already accepts `?access_token=` for any path ending in `/stream`. `/api/notifications/stream` matches; no extra work needed.
- **Stream backpressure.** Unbounded channels mean the server's memory grows if the client stops reading. Bound to 1024 if abuse appears; v1 unbounded is fine for typical "≤200 events per session" loads.
- **Orphan tolerance.** `NotificationsQueryService` skips rows whose project or work item has gone missing. Logs at `Information` so FEAT-006 can surface them.
- **`Response.StartAsync` ordering.** Headers MUST be set before `StartAsync`. The pattern matches T-037.

## Acceptance Verification
- [ ] `dotnet build` clean.
- [ ] Manual smoke: `curl -N -H "Authorization: Bearer ..." http://localhost:5000/api/notifications/stream` stays open; triggering a transition in another terminal emits an SSE chunk.
- [ ] `GET /api/notifications/pending` returns the operator's pending list.
- [ ] T-045 covers the AC assertions.
