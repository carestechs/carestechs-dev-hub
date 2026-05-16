using DevHub.Contracts.Executors;
using Microsoft.AspNetCore.Http;

namespace DevHub.Modules.WorkItems.Services;

/// <summary>
/// Owns the open-and-copy loop for SSE pass-through. Authorization happens at the controller
/// BEFORE calling Pipe; on auth deny the upstream socket is never opened (AC-1).
/// </summary>
public sealed class WorkItemStreamForwarder(IExecutorHttpClient client)
{
    public async Task PipeAsync(
        HttpContext ctx,
        ExecutorRegistrationDescriptor executor,
        string correlationMarker,
        CancellationToken cancellationToken)
    {
        // Headers MUST be set BEFORE any body bytes are written.
        ctx.Response.StatusCode = 200;
        ctx.Response.Headers["Content-Type"] = "text/event-stream";
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";
        await ctx.Response.StartAsync(cancellationToken);

        await using var conn = await client.OpenStreamAsync(executor, correlationMarker, cancellationToken);

        // 8 KiB buffer: large enough to avoid per-byte syscalls, small enough that a chunk
        // emitted by the executor flushes promptly.
        var buffer = new byte[8 * 1024];
        int read;
        while ((read = await conn.Stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            await ctx.Response.Body.FlushAsync(cancellationToken);
        }
    }
}
