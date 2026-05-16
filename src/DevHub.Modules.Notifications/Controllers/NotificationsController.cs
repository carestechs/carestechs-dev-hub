using System.Text;
using System.Text.Json;
using DevHub.Contracts;
using DevHub.Contracts.Identity;
using DevHub.Modules.Notifications.DTOs;
using DevHub.Modules.Notifications.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Modules.Notifications.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(
    INotificationsQueryService query,
    PendingActionStreamRegistry registry,
    ICurrentMember me) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken ct) =>
        Ok(new EnvelopeDto<IReadOnlyList<PendingActionDto>>(
            await query.ListPendingForMemberAsync(me.MemberId, ct)));

    /// <summary>
    /// SSE pass-through of newly raised/dismissed pending actions for the caller. No replay on
    /// connect — the client refetches /pending to resync (AC-3 + AC-5).
    /// </summary>
    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        Response.StatusCode = 200;
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.StartAsync(ct);

        await using var subscription = registry.Subscribe(me.MemberId, out var reader);

        try
        {
            await foreach (var ev in reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(ev, JsonOptions);
                var bytes = Encoding.UTF8.GetBytes($"data: {json}\n\n");
                await Response.Body.WriteAsync(bytes, ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* client disconnect */ }
    }
}
