# Implementation Plan: T-008 — Health endpoint with DB check

## Task Reference
- **Task ID:** T-008
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** S
- **Rationale:** FEAT-001 AC-1; Docker Compose's `depends_on: condition: service_healthy` target.

## Overview
Expose `GET /health` with the contracted JSON shape, backed by ASP.NET Core's health-check service running a Postgres connectivity check.

## Implementation Steps

### Step 1: Register a DB health check
**File:** `src/Portfolio.Api/Program.cs`
**Action:** Modify
Inside `builder.Services`:
```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<WorkspaceDbContext>(name: "db", failureStatus: HealthStatus.Unhealthy);
```
(Workspace's DbContext is guaranteed to exist after T-005.)

### Step 2: Custom response writer
**File:** `src/Portfolio.Api/HealthCheckResponseWriter.cs`
**Action:** Create
```csharp
public static class HealthCheckResponseWriter
{
    public static Task WriteAsync(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status == HealthStatus.Healthy ? "ok" : "degraded",
            checks = report.Entries.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Status == HealthStatus.Healthy ? "up" : "down")
        };
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
```

### Step 3: Map the endpoint
**File:** `src/Portfolio.Api/Program.cs`
**Action:** Modify
After `app.MapControllers()`:
```csharp
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
}).AllowAnonymous();
```

### Step 4: Smoke
**Action:** Verify
Start API. `curl http://localhost:5000/health` → 200 + `{"status":"ok","checks":{"db":"up"}}`. Stop Postgres → `curl` returns 503 + `{"status":"degraded","checks":{"db":"down"}}`.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `src/Portfolio.Api/HealthCheckResponseWriter.cs` | Create | Custom JSON shape |
| `src/Portfolio.Api/Program.cs` | Modify | Register check + map endpoint |

## Edge Cases & Risks
- **`AddDbContextCheck` opens a real connection** — fast in normal operation but can hang on a network partition. Default timeout is acceptable for the dev/healthcheck use case; production tuning is a later concern.
- **CORS preflight on `/health`** — endpoint is `AllowAnonymous` and outside the SPA's normal API surface; tooling that probes `/health` from another origin will trigger CORS preflight. Add `OPTIONS` handling implicitly via `UseCors()` order, which is already correct (CORS runs before mapping endpoints).

## Acceptance Verification
- [ ] `GET /health` returns 200 with `{"status":"ok","checks":{"db":"up"}}` when Postgres is reachable.
- [ ] `GET /health` returns 503 with `{"status":"degraded","checks":{"db":"down"}}` when Postgres is stopped.
- [ ] Endpoint requires no authentication.
