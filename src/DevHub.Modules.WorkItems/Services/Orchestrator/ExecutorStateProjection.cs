using System.Text;
using System.Text.Json;

namespace DevHub.Modules.WorkItems.Services.Orchestrator;

/// <summary>
/// FEAT-010: builds the opaque <c>executorState</c> JSON that DevHub stores on
/// <c>WorkItemDto.ExecutorState</c>, and replays <c>assignment-confirmed</c> signals
/// from the orchestrator's trace into a <c>{ taskId → assignee }</c> map (FEAT-009 source).
///
/// FEAT-011 (T-098): enriched to build a <c>steps</c> array from trace records,
/// with per-step <c>nodeResult</c> (stripped of <c>__memory_patch</c>) so the frontend
/// panels can read artefact data from preceding LLM steps.
/// </summary>
internal static class ExecutorStateProjection
{
    private static readonly Dictionary<string, string> DisplayLabels = new(StringComparer.Ordinal)
    {
        ["brief-confirmed"] = "Brief Review",
        ["tasks-confirmed"] = "Task List",
        ["assignment-confirmed"] = "Task Assignment",
        ["plan-confirmed"] = "Plan Review",
        ["implementation-complete"] = "Implementation Review",
        ["review-completed"] = "Final Review",
    };

    /// <summary>
    /// Assembles <c>{ runId, agentRef, steps, assignments, stopReason }</c>.
    /// </summary>
    public static JsonElement Build(
        Guid runId,
        string agentRef,
        IReadOnlyList<JsonElement> steps,
        IReadOnlyDictionary<string, string> assignments,
        string? stopReason)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"runId\":");
        sb.Append(JsonSerializer.Serialize(runId.ToString()));
        sb.Append(",\"agentRef\":");
        sb.Append(JsonSerializer.Serialize(agentRef));
        sb.Append(",\"steps\":[");
        for (var i = 0; i < steps.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(steps[i].GetRawText());
        }
        sb.Append(']');
        sb.Append(",\"assignments\":");
        sb.Append(JsonSerializer.Serialize(assignments));
        sb.Append(",\"stopReason\":");
        sb.Append(stopReason is null ? "null" : JsonSerializer.Serialize(stopReason));
        sb.Append('}');
        return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
    }

    /// <summary>
    /// Builds the <c>steps</c> array from <c>kind == "step"</c> trace records.
    /// Each entry carries <c>key</c> (checkpoint derivation), <c>nodeName</c>,
    /// <c>label</c>, <c>stepNumber</c>, <c>status</c>, and <c>nodeResult</c>
    /// (with <c>__memory_patch</c> stripped).
    /// </summary>
    public static List<JsonElement> BuildSteps(
        IEnumerable<JsonElement> traceRecords,
        string? currentCheckpointKey)
    {
        var steps = new List<JsonElement>();
        foreach (var rec in traceRecords)
        {
            if (rec.ValueKind != JsonValueKind.Object) continue;
            if (!rec.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String) continue;
            if (kind.GetString() != "step") continue;
            if (!rec.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) continue;

            var nodeName = data.TryGetProperty("nodeName", out var nn) && nn.ValueKind == JsonValueKind.String
                ? nn.GetString() : null;
            var stepNumber = data.TryGetProperty("stepNumber", out var sn) && sn.ValueKind == JsonValueKind.Number
                ? sn.GetInt32() : 0;
            var rawStatus = data.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                ? st.GetString() : "pending";

            var checkpointKey = CheckpointDerivation.SignalForNodeName(nodeName);
            var label = checkpointKey is not null && DisplayLabels.TryGetValue(checkpointKey, out var l)
                ? l : nodeName ?? "unknown";

            var isActive = checkpointKey is not null
                && checkpointKey == currentCheckpointKey
                && (rawStatus == "dispatched" || rawStatus == "pending");
            var status = isActive ? "active"
                : rawStatus == "completed" ? "completed"
                : rawStatus == "failed" ? "failed"
                : "pending";

            var nodeResult = data.TryGetProperty("nodeResult", out var nr) && nr.ValueKind == JsonValueKind.Object
                ? StripMemoryPatch(nr) : (JsonElement?)null;
            var nodeInputs = data.TryGetProperty("nodeInputs", out var ni) && ni.ValueKind == JsonValueKind.Object
                ? ni : (JsonElement?)null;

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"key\":");
            sb.Append(checkpointKey is null ? "null" : JsonSerializer.Serialize(checkpointKey));
            sb.Append(",\"nodeName\":");
            sb.Append(nodeName is null ? "null" : JsonSerializer.Serialize(nodeName));
            sb.Append(",\"label\":");
            sb.Append(JsonSerializer.Serialize(label));
            sb.Append(",\"stepNumber\":");
            sb.Append(stepNumber);
            sb.Append(",\"status\":");
            sb.Append(JsonSerializer.Serialize(status));
            sb.Append(",\"nodeInputs\":");
            sb.Append(nodeInputs.HasValue ? nodeInputs.Value.GetRawText() : "null");
            sb.Append(",\"nodeResult\":");
            sb.Append(nodeResult.HasValue ? nodeResult.Value.GetRawText() : "null");
            sb.Append('}');

            steps.Add(JsonDocument.Parse(sb.ToString()).RootElement.Clone());
        }

        return steps;
    }

    /// <summary>
    /// Returns a copy of the object with <c>__memory_patch</c> removed.
    /// </summary>
    private static JsonElement StripMemoryPatch(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object) return obj;

        var hasKey = false;
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name == "__memory_patch") { hasKey = true; break; }
        }
        if (!hasKey) return obj;

        var sb = new StringBuilder();
        sb.Append('{');
        var first = true;
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name == "__memory_patch") continue;
            if (!first) sb.Append(',');
            sb.Append(JsonSerializer.Serialize(prop.Name));
            sb.Append(':');
            sb.Append(prop.Value.GetRawText());
            first = false;
        }
        sb.Append('}');
        return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
    }

    /// <summary>
    /// Walks a trace NDJSON record list and replays every <c>operator_signal</c>-kind
    /// record whose <c>name == "assignment-confirmed"</c> into a <c>taskId → assignee</c>
    /// map (last-write-wins).
    /// </summary>
    public static Dictionary<string, string> ParseAssignmentsFromTrace(IEnumerable<JsonElement> traceRecords)
    {
        var result = new Dictionary<string, string>();
        foreach (var rec in traceRecords)
        {
            if (rec.ValueKind != JsonValueKind.Object) continue;
            if (!rec.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String) continue;
            if (kind.GetString() != "operator_signal") continue;
            if (!rec.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) continue;
            if (!data.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            if (name.GetString() != "assignment-confirmed") continue;
            if (!data.TryGetProperty("taskId", out var task) || task.ValueKind != JsonValueKind.String) continue;
            if (!data.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) continue;
            if (!payload.TryGetProperty("assignee", out var assignee) || assignee.ValueKind != JsonValueKind.String) continue;
            var assigneeStr = assignee.GetString();
            if (string.IsNullOrEmpty(assigneeStr)) continue;
            result[task.GetString()!] = assigneeStr;
        }
        return result;
    }

    /// <summary>
    /// Returns the most recent <c>step</c>-kind trace record's
    /// <c>data.nodeInputs.taskId</c>, or <c>null</c>.
    /// </summary>
    public static string? LatestStepTaskId(IEnumerable<JsonElement> traceRecords)
    {
        JsonElement? latestStepData = null;
        foreach (var rec in traceRecords)
        {
            if (rec.ValueKind != JsonValueKind.Object) continue;
            if (!rec.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String) continue;
            if (kind.GetString() != "step") continue;
            if (!rec.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) continue;
            latestStepData = data;
        }
        if (latestStepData is null) return null;
        if (!latestStepData.Value.TryGetProperty("nodeInputs", out var nodeInputs)
            || nodeInputs.ValueKind != JsonValueKind.Object) return null;
        if (!nodeInputs.TryGetProperty("taskId", out var taskId)
            || taskId.ValueKind != JsonValueKind.String) return null;
        var s = taskId.GetString();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
