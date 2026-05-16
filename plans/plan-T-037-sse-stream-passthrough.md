# Implementation Plan: T-037 — SSE pass-through stream endpoint

## Task Reference
- **Task ID:** T-037 · **Type:** Backend · **Workflow:** standard · **Complexity:** M
- **Rationale:** AC-4 — "live state passes through." The whole DevHub value claim depends on this hot path being un-buffered, byte-for-byte.

## Overview
`GET /api/projects/{projectId}/work-items/{workItemId}/stream` — open upstream stream after auth, pipe bytes to `HttpContext.Response.Body` until either side disconnects.

## Implementation Steps

### Step 1: Forwarder
**File:** `src/DevHub.Modules.WorkItems/Services/WorkItemStreamForwarder.cs` · Create

```csharp
internal sealed class WorkItemStreamForwarder(IExecutorHttpClient client)
{
    public async Task PipeAsync(HttpContext ctx, ExecutorRegistrationDescriptor executor, string marker, CancellationToken ct)
    {
        // Headers MUST be set before the first write.
        ctx.Response.StatusCode = 200;
        ctx.Response.Headers.Append("Content-Type", "text/event-stream");
        ctx.Response.Headers.Append("Cache-Control", "no-store");
        ctx.Response.Headers.Append("X-Accel-Buffering", "no");
        await ctx.Response.StartAsync(ct);

        await using var upstream = await client.OpenStreamAsync(executor, marker, ct);
        // Buffer small enough that flushes are meaningful; large enough to avoid per-byte syscalls.
        var buffer = new byte[8192];
        int read;
        while ((read = await upstream.ReadAsync(buffer, ct)) > 0)
        {
            await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
}
```

`IExecutorHttpClient.OpenStreamAsync` returns an `IAsyncDisposable & Stream`-like wrapper that owns both the HTTP response and the response stream — disposing closes both.

### Step 2: Controller action
**File:** `src/DevHub.Modules.WorkItems/Controllers/WorkItemsController.cs` · Modify

```csharp
[HttpGet("{id:guid}/stream")]
public async Task Stream(Guid projectId, Guid id, [FromServices] WorkItemStreamForwarder forwarder, CancellationToken ct)
{
    // 1. Authorize (project:any) — audit Granted/Denied via IProjectAuthorizationService.
    await authz.EnsureAuthorizedAsync(me.MemberId, projectId, "workitem:stream", requiredRoleKey: null, ct);

    // 2. Resolve the work item + executor descriptor.
    var workItem = await db.WorkItems.AsNoTracking()
        .FirstOrDefaultAsync(w => w.Id == id && w.ProjectId == projectId, ct)
        ?? throw new NotFoundException("Work item not found.");
    var executor = await router.ResolveAsync(projectId, ct)
        ?? throw new ConflictException("Project has no executor bound.");

    // 3. Authorize succeeded — only NOW open the upstream socket. CRITICAL for AC-1.
    await forwarder.PipeAsync(HttpContext, executor, workItem.ExecutorCorrelationMarker, ct);
}
```

Returning `Task` (not `IActionResult`) bypasses MVC formatters — no accidental buffering.

### Step 3: Allow access token via query (SSE workaround)
`EventSource` in the browser can't send `Authorization: Bearer`. Add a JWT bearer "events" enricher that also accepts `?access_token=...` for the stream paths:

**File:** `src/DevHub.Api/Program.cs` · Modify

```csharp
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((o, cfg) =>
    {
        // ... existing token validation params ...
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (string.IsNullOrEmpty(ctx.Token) && ctx.HttpContext.Request.Path.Value?.EndsWith("/stream", StringComparison.Ordinal) == true)
                {
                    ctx.Token = ctx.Request.Query["access_token"];
                }
                return Task.CompletedTask;
            },
        };
    });
```

### Step 4: DI
**File:** `src/DevHub.Modules.WorkItems/WorkItemsModuleExtensions.cs` · Modify
```csharp
services.AddScoped<WorkItemStreamForwarder>();
```

## Files Affected
| File | Action |
|------|--------|
| `WorkItems/Services/WorkItemStreamForwarder.cs` | Create |
| `WorkItems/Controllers/WorkItemsController.cs` | Modify (add `Stream` action) |
| `Api/Program.cs` | Modify (JwtBearerEvents.OnMessageReceived) |
| `WorkItems/WorkItemsModuleExtensions.cs` | Modify |

## Edge Cases & Risks
- **Header set after first write.** ASP.NET throws if `Response.Headers.Append` is called after the response has started. `Response.StartAsync()` makes the contract explicit. Setting headers before any `Body.WriteAsync` is the discipline.
- **Server-side disconnect.** If the upstream closes, `ReadAsync` returns 0; the loop exits and we end the response cleanly.
- **Client-side disconnect.** `HttpContext.RequestAborted` cancels the `ReadAsync`/`WriteAsync`; the `await using` over `upstream` closes the upstream HTTP socket.
- **Buffering at proxies.** `X-Accel-Buffering: no` is the nginx hint (we ship `client/nginx.conf` with `proxy_buffering off` for the SPA dev nginx; this header covers prod proxies too).
- **`?access_token=` in URL logs.** Logs are configured to redact `access_token` query param? Verify in T-036/T-037 review. v1: document the trade-off; FEAT-006 can ship structured-log redaction.
- **Audit row per connection.** Written by `EnsureAuthorizedAsync`; one row per stream open. We do NOT write per chunk.

## Acceptance Verification
- [ ] `dotnet build` clean.
- [ ] Manual smoke: `curl -N -H "Authorization: Bearer ..." http://localhost:5000/api/projects/{id}/work-items/{wid}/stream` returns chunks as the fake executor emits them.
- [ ] T-038 ships the chunk-by-chunk arrival test + the "deny doesn't open upstream" test.
