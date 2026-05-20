using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DevHub.TestHarness.FakeExecutor;  // CallRecord
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevHub.TestHarness.FakeOrchestrator;

/// <summary>
/// FEAT-010 / T-088: throwaway Kestrel host that pretends to be the carestechs-agent-orchestrator's
/// <c>/api/v1/runs</c> API. Sibling to <see cref="FakeExecutorHost"/>; both serve different
/// protocols. Bound to a random local port; tests compose <see cref="BaseUrl"/> into a seeded
/// <c>ExecutorRegistration</c> with <c>protocol = "orchestrator"</c>.
/// </summary>
public sealed class FakeOrchestratorHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string BaseUrl { get; }
    public ConcurrentQueue<CallRecord> Calls { get; } = new();
    public ScriptedRunResponses Scripted { get; } = new();

    /// <summary>Tracks the run ids handed out by <c>POST /runs</c> so tests can assert on them.</summary>
    public ConcurrentQueue<Guid> CreatedRunIds { get; } = new();

    private FakeOrchestratorHost(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public static async Task<FakeOrchestratorHost> StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        var marker = new FakeOrchestratorHostMarker();
        Map(app, marker);

        await app.StartAsync(cancellationToken);
        var url = app.Urls.FirstOrDefault() ?? throw new InvalidOperationException("FakeOrchestrator failed to bind.");
        var fake = new FakeOrchestratorHost(app, url);
        marker.Owner = fake;
        return fake;
    }

    private static void Map(WebApplication app, FakeOrchestratorHostMarker marker)
    {
        // POST /api/v1/runs — create run
        app.MapPost("/api/v1/runs", async (HttpContext ctx) =>
        {
            await marker.RecordAsync(ctx);
            ctx.Response.StatusCode = marker.Owner!.Scripted.CreateRunHttpStatusCode;
            var runId = Guid.NewGuid();
            marker.Owner.CreatedRunIds.Enqueue(runId);
            await ctx.Response.WriteAsJsonAsync(new
            {
                data = new
                {
                    id = runId,
                    agentRef = "lifecycle-agent@0.4.0-manual",
                    status = marker.Owner.Scripted.CurrentRunStatus,
                    startedAt = DateTimeOffset.UtcNow,
                    endedAt = (DateTimeOffset?)null,
                },
                meta = (object?)null,
            });
        });

        // GET /api/v1/runs/{run_id} — fetch detail
        app.MapGet("/api/v1/runs/{runId:guid}", async (Guid runId, HttpContext ctx) =>
        {
            await marker.RecordAsync(ctx);
            return Results.Json(new
            {
                data = new
                {
                    id = runId,
                    agentRef = "lifecycle-agent@0.4.0-manual",
                    agentDefinitionHash = "test-hash",
                    intake = new { },
                    status = marker.Owner!.Scripted.CurrentRunStatus,
                    stopReason = marker.Owner.Scripted.StopReason,
                    startedAt = DateTimeOffset.UtcNow,
                    endedAt = (DateTimeOffset?)null,
                    traceUri = $"{marker.Owner.BaseUrl}/api/v1/runs/{runId}/trace",
                    stepCount = marker.Owner.Scripted.LastStep is null ? 0 : marker.Owner.Scripted.LastStep.StepNumber,
                    lastStep = marker.Owner.Scripted.LastStep,
                },
                meta = (object?)null,
            });
        });

        // POST /api/v1/runs/{run_id}/signals — append signal
        app.MapPost("/api/v1/runs/{runId:guid}/signals", async (Guid runId, HttpContext ctx) =>
        {
            await marker.RecordAsync(ctx);
            var body = await ReadBodyAsync(ctx);

            // Auto-append the signal as a trace record so subsequent fetches see it in
            // ParseAssignmentsFromTrace. Tests can also pre-seed TraceRecords directly
            // for setup-only scenarios.
            if (body.HasValue)
            {
                var name = body.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() ?? string.Empty : string.Empty;
                var taskId = body.Value.TryGetProperty("taskId", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() : null;
                object? payload = null;
                if (body.Value.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object)
                {
                    payload = JsonSerializer.Deserialize<JsonElement>(p.GetRawText());
                }
                marker.Owner!.Scripted.TraceRecords.Add(TraceRecord.OperatorSignal(name, taskId, payload));
            }

            ctx.Response.StatusCode = marker.Owner!.Scripted.SignalHttpStatusCode;
            await ctx.Response.WriteAsJsonAsync(new
            {
                data = new
                {
                    id = Guid.NewGuid(),
                    runId,
                    name = body?.TryGetProperty("name", out var nn) == true ? nn.GetString() : null,
                    taskId = body?.TryGetProperty("taskId", out var tt) == true ? tt.GetString() : null,
                    createdAt = DateTimeOffset.UtcNow,
                },
                meta = (object?)null,
            });
        });

        // POST /api/v1/runs/{run_id}/cancel — cancel
        app.MapPost("/api/v1/runs/{runId:guid}/cancel", async (Guid runId, HttpContext ctx) =>
        {
            await marker.RecordAsync(ctx);
            marker.Owner!.Scripted.CurrentRunStatus = "cancelled";
            marker.Owner.Scripted.StopReason = "cancelled";
            ctx.Response.StatusCode = marker.Owner.Scripted.CancelHttpStatusCode;
            await ctx.Response.WriteAsJsonAsync(new
            {
                data = new { id = runId, status = "cancelled" },
                meta = (object?)null,
            });
        });

        // GET /api/v1/runs/{run_id}/trace — NDJSON (no follow in this fake; emit + close)
        app.MapGet("/api/v1/runs/{runId:guid}/trace", async (Guid runId, HttpContext ctx) =>
        {
            await marker.RecordAsync(ctx);
            ctx.Response.Headers["Content-Type"] = "application/x-ndjson";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            foreach (var rec in marker.Owner!.Scripted.TraceRecords)
            {
                // BUG-001 / T-095: real orchestrator wraps every record as
                // {"kind":"...","data":{...}} — see service.py § _serialize_trace_record.
                var json = JsonSerializer.Serialize(new { kind = rec.Kind, data = rec.Data });
                var bytes = Encoding.UTF8.GetBytes(json + "\n");
                await ctx.Response.Body.WriteAsync(bytes, ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
        });
    }

    public int CountByPath(string pathFragment) =>
        Calls.Count(c => c.Path.Contains(pathFragment, StringComparison.Ordinal));

    public int Total => Calls.Count;

    public void ResetCalls()
    {
        while (Calls.TryDequeue(out _)) { }
    }

    public ValueTask DisposeAsync() => new(_app.DisposeAsync().AsTask());

    private static async Task<JsonElement?> ReadBodyAsync(HttpContext ctx)
    {
        if (ctx.Request.ContentLength == 0) return null;
        using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
        var raw = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class FakeOrchestratorHostMarker
    {
        public FakeOrchestratorHost? Owner { get; set; }

        public async Task RecordAsync(HttpContext ctx)
        {
            if (Owner is null) return;
            string? body = null;
            if (ctx.Request.Method is "POST" or "PUT" or "PATCH")
            {
                ctx.Request.EnableBuffering();
                using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
                body = await reader.ReadToEndAsync();
                ctx.Request.Body.Position = 0;
            }
            var auth = ctx.Request.Headers["X-API-Key"].ToString();
            Owner.Calls.Enqueue(new CallRecord(
                ctx.Request.Method, ctx.Request.Path, CorrelationMarker: ctx.Request.Path,
                Authorization: auth, BodyJson: body, OccurredAt: DateTimeOffset.UtcNow));
        }
    }
}
