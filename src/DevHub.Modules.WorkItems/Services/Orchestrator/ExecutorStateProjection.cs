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
    /// Walks a trace NDJSON record list and replays every <c>operator_signal</c>-kind
    /// record whose <c>name == "assignment-confirmed"</c> into a <c>taskId → assignee</c>
    /// map (last-write-wins).
    ///
    /// On-the-wire trace shape is <c>{"kind":"...","data":{...DTO fields by alias...}}</c>
    /// (see <c>carestechs-agent-orchestrator/src/app/modules/ai/service.py</c>
    /// §<c>_serialize_trace_record</c>). The orchestrator's signal kind is
    /// <c>operator_signal</c>, not <c>signal</c> (see <c>_KIND_BY_TYPE</c> at
    /// <c>service.py:73-78</c>). Empirical evidence: orchestrator's own
    /// <c>tests/integration/test_lifecycle_anthropic_mocked.py:239</c> asserts
    /// <c>"kind":"operator_signal"</c> in trace lines.
    ///
    /// Records with the wrong kind, missing <c>data</c>, missing taskId, non-string
    /// assignee, or empty assignee are skipped.
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
    /// <c>data.nodeInputs.taskId</c>, or <c>null</c> when no step has been dispatched
    /// or the latest step lacks the field.
    ///
    /// This is DevHub's primary derivation for <c>currentTaskId</c> in the orchestrator
    /// protocol: the orchestrator's deterministic runtime injects
    /// <c>nodeInputs.taskId = LifecycleMemory.current_task_id</c> at dispatch time
    /// (<c>carestechs-agent-orchestrator/src/app/modules/ai/runtime_deterministic.py:246</c>),
    /// so the latest step's <c>nodeInputs.taskId</c> IS the awaited task id at a pause.
    ///
    /// On-the-wire trace shape is <c>{"kind":"step","data":{...StepDto fields by alias...}}</c>
    /// — verified by <c>carestechs-agent-orchestrator/tests/test_cli_runs.py:246-256</c>.
    ///
    /// Conservative on edge cases: returns <c>null</c> rather than walking back to an
    /// older step whose <c>nodeInputs</c> is intact, to avoid surfacing a stale id
    /// during a transient state.
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
