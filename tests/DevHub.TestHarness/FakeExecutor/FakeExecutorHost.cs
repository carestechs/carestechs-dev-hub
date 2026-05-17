using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevHub.TestHarness.FakeExecutor;

/// <summary>
/// A throwaway Kestrel host that pretends to be a lifecycle executor. Bound to a random local
/// port so tests can compose <see cref="BaseUrl"/> into a seeded <c>ExecutorRegistration</c>.
/// Every HTTP call is recorded; tests assert against <see cref="Calls"/> for AC-1 (no outbound
/// HTTP on deny) and against the live-counter for AC-4 (disconnect cleanup).
/// </summary>
public sealed class FakeExecutorHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string BaseUrl { get; }
    public ConcurrentQueue<CallRecord> Calls { get; } = new();
    public ScriptedResponses Scripted { get; } = new();
    /// Open SSE connections held by the fake. Used by AC-4 disconnect tests.
    public int OpenStreamConnections;

    private FakeExecutorHost(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public static async Task<FakeExecutorHost> StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        var marker = new FakeExecutorHostMarker();
        Map(app, marker);

        await app.StartAsync(cancellationToken);
        var url = app.Urls.FirstOrDefault() ?? throw new InvalidOperationException("Fake executor failed to bind.");
        var fake = new FakeExecutorHost(app, url);
        marker.Owner = fake;
        return fake;
    }

    private static void Map(WebApplication app, FakeExecutorHostMarker marker)
    {
        // POST /work-items — start
        app.MapPost("/work-items", async (HttpContext ctx) =>
        {
            await marker.RecordAsync(ctx);
            return Results.Json(new
            {
                currentStatus = marker.Owner!.Scripted.StartStatus,
                currentCheckpointKey = marker.Owner.Scripted.StartCheckpointKey,
                executorState = marker.Owner.Scripted.StartExecutorState,
                currentTaskId = marker.Owner.Scripted.CurrentTaskId,
            });
        });

        // GET /work-items/{marker} — fetch
        app.MapGet("/work-items/{correlationMarker}", async (string correlationMarker, HttpContext ctx) =>
        {
            await marker.RecordAsync(ctx, correlationMarker);
            return Results.Json(new
            {
                currentStatus = marker.Owner!.Scripted.FetchStatus,
                currentCheckpointKey = marker.Owner.Scripted.FetchCheckpointKey,
                executorState = marker.Owner.Scripted.FetchExecutorState,
                currentTaskId = marker.Owner.Scripted.CurrentTaskId,
            });
        });

        // POST /work-items/{marker}/checkpoints/{key}/signal
        app.MapPost("/work-items/{correlationMarker}/checkpoints/{checkpointKey}/signal",
            async (string correlationMarker, string checkpointKey, HttpContext ctx) =>
        {
            await marker.RecordAsync(ctx, correlationMarker);
            var status = marker.Owner!.Scripted.SignalHttpStatusCode;
            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(new
            {
                currentStatus = marker.Owner.Scripted.SignalStatus,
                currentCheckpointKey = marker.Owner.Scripted.SignalCheckpointKey,
                executorState = marker.Owner.Scripted.SignalExecutorState,
                httpStatus = status,
                currentTaskId = marker.Owner.Scripted.CurrentTaskId,
            });
        });

        // POST /work-items/{marker}/cancel
        app.MapPost("/work-items/{correlationMarker}/cancel", async (string correlationMarker, HttpContext ctx) =>
        {
            await marker.RecordAsync(ctx, correlationMarker);
            ctx.Response.StatusCode = 204;
        });

        // GET /work-items/{marker}/stream
        app.MapGet("/work-items/{correlationMarker}/stream", async (string correlationMarker, HttpContext ctx) =>
        {
            await marker.RecordAsync(ctx, correlationMarker);
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            Interlocked.Increment(ref marker.Owner!.OpenStreamConnections);
            try
            {
                foreach (var (chunk, delay) in marker.Owner.Scripted.StreamChunks)
                {
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, ctx.RequestAborted);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(chunk);
                    await ctx.Response.Body.WriteAsync(bytes, ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
            }
            catch (OperationCanceledException) { /* client disconnect */ }
            finally
            {
                Interlocked.Decrement(ref marker.Owner.OpenStreamConnections);
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

    /// <summary>
    /// Minimal helper that holds the link from the per-request endpoints back to the owning host.
    /// </summary>
    private sealed class FakeExecutorHostMarker
    {
        public FakeExecutorHost? Owner { get; set; }

        public async Task RecordAsync(HttpContext ctx, string? correlationMarker = null)
        {
            if (Owner is null) return;
            // Always attempt to capture the body for non-trivial methods. ContentLength is
            // unreliable with HttpClient.PostAsJsonAsync (often null / chunked), so we just
            // read whatever's there; the empty-body case yields an empty string.
            string? body = null;
            if (ctx.Request.Method is "POST" or "PUT" or "PATCH")
            {
                ctx.Request.EnableBuffering();
                using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
                body = await reader.ReadToEndAsync();
                ctx.Request.Body.Position = 0;
            }
            var auth = ctx.Request.Headers.Authorization.ToString();
            var marker = correlationMarker
                ?? ctx.Request.Headers["X-DevHub-Correlation"].ToString();
            Owner.Calls.Enqueue(new CallRecord(
                ctx.Request.Method, ctx.Request.Path, marker, auth, body, DateTimeOffset.UtcNow));
        }
    }
}
