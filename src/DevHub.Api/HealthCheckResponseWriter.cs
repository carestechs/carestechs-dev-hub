using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DevHub.Api;

/// Writes the response shape contracted in api-spec.md §Operations:
/// <c>{ "status": "ok|degraded", "checks": { "&lt;name&gt;": "up|down" } }</c>.
internal static class HealthCheckResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status == HealthStatus.Healthy ? "ok" : "degraded",
            checks = report.Entries.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Status == HealthStatus.Healthy ? "up" : "down")
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
