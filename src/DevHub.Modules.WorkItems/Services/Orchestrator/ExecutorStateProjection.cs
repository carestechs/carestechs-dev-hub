using System.Text;
using System.Text.Json;

namespace DevHub.Modules.WorkItems.Services.Orchestrator;

/// <summary>
/// FEAT-010: builds the opaque <c>executorState</c> JSON that DevHub stores on
/// <c>WorkItemDto.ExecutorState</c>, and replays <c>assignment-confirmed</c> signals
/// from the orchestrator's trace into a <c>{ taskId → assignee }</c> map (FEAT-009 source).
/// </summary>
internal static class ExecutorStateProjection
{
    /// <summary>
    /// Assembles <c>{ runId, agentRef, lastStep, assignments, stopReason }</c>.
    /// </summary>
    public static JsonElement Build(
        Guid runId,
        string agentRef,
        JsonElement? lastStep,
        IReadOnlyDictionary<string, string> assignments,
        string? stopReason)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"runId\":");
        sb.Append(JsonSerializer.Serialize(runId.ToString()));
        sb.Append(",\"agentRef\":");
        sb.Append(JsonSerializer.Serialize(agentRef));
        sb.Append(",\"lastStep\":");
        sb.Append(lastStep is null ? "null" : lastStep.Value.GetRawText());
        sb.Append(",\"assignments\":");
        sb.Append(JsonSerializer.Serialize(assignments));
        sb.Append(",\"stopReason\":");
        sb.Append(stopReason is null ? "null" : JsonSerializer.Serialize(stopReason));
        sb.Append('}');
        return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
    }

    /// <summary>
    /// Walks a trace NDJSON record list, picks every <c>signal</c>-kind record whose
    /// name is <c>assignment-confirmed</c>, and replays them into a <c>taskId → assignee</c>
    /// map. Later signals for the same task overwrite earlier ones (last-write-wins —
    /// matches the orchestrator's own behavior when an operator re-confirms an assignment).
    /// Records with missing taskId, non-string assignee, or empty assignee are skipped.
    /// </summary>
    public static Dictionary<string, string> ParseAssignmentsFromTrace(IEnumerable<JsonElement> traceRecords)
    {
        var result = new Dictionary<string, string>();
        foreach (var rec in traceRecords)
        {
            if (rec.ValueKind != JsonValueKind.Object) continue;
            if (!rec.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String) continue;
            if (kind.GetString() != "signal") continue;
            if (!rec.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            if (name.GetString() != "assignment-confirmed") continue;
            if (!rec.TryGetProperty("taskId", out var task) || task.ValueKind != JsonValueKind.String) continue;
            if (!rec.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) continue;
            if (!payload.TryGetProperty("assignee", out var assignee) || assignee.ValueKind != JsonValueKind.String) continue;
            var assigneeStr = assignee.GetString();
            if (string.IsNullOrEmpty(assigneeStr)) continue;
            result[task.GetString()!] = assigneeStr;
        }
        return result;
    }

    /// <summary>
    /// Returns the most recent <c>signal</c>-kind record's <c>taskId</c>, or null when
    /// the trace is empty or no signals carry a task id. Used as the fallback for
    /// <c>currentTaskId</c> when <c>lastStep</c> doesn't expose it.
    /// </summary>
    public static string? LatestSignalTaskId(IEnumerable<JsonElement> traceRecords)
    {
        string? last = null;
        foreach (var rec in traceRecords)
        {
            if (rec.ValueKind != JsonValueKind.Object) continue;
            if (!rec.TryGetProperty("kind", out var kind) || kind.GetString() != "signal") continue;
            if (rec.TryGetProperty("taskId", out var t) && t.ValueKind == JsonValueKind.String)
            {
                last = t.GetString();
            }
        }
        return last;
    }
}
