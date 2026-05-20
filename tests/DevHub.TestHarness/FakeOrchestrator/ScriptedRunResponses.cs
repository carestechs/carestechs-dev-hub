namespace DevHub.TestHarness.FakeOrchestrator;

/// <summary>
/// FEAT-010 / T-088: test-tunable response shapes for the FakeOrchestrator.
/// Mutating these between requests is how tests simulate run transitions
/// (running → paused → completed, currentTaskId advancing, etc.).
/// </summary>
public sealed class ScriptedRunResponses
{
    public string CurrentRunStatus { get; set; } = "running";  // RunStatus enum values

    /// <summary>
    /// The orchestrator's last_step. <c>nodeName</c> drives DevHub's checkpoint-key
    /// derivation per <c>CheckpointDerivation</c> conventions (e.g. <c>confirm_assignment</c>
    /// → <c>assignment-confirmed</c>). Null means no step has run yet.
    /// </summary>
    public LastStepDto? LastStep { get; set; }

    public string? StopReason { get; set; }

    /// <summary>
    /// NDJSON trace records emitted by <c>/api/v1/runs/{id}/trace</c>. Tests append
    /// to this list to model signal history, awaiting_signal records, etc.
    /// </summary>
    public List<TraceRecord> TraceRecords { get; set; } = new();

    public int CreateRunHttpStatusCode { get; set; } = 202;
    public int SignalHttpStatusCode { get; set; } = 202;
    public int CancelHttpStatusCode { get; set; } = 200;
}

public sealed record LastStepDto(Guid Id, int StepNumber, string NodeName, string Status);

/// <summary>
/// BUG-001 / T-095: mirrors the orchestrator's on-the-wire NDJSON shape, which is
/// <c>{"kind":"...","data":{...DTO fields by alias...}}</c> per
/// <c>carestechs-agent-orchestrator/src/app/modules/ai/service.py</c>
/// §<c>_serialize_trace_record</c>. Use the static factories
/// (<see cref="Step"/>, <see cref="OperatorSignal"/>) instead of constructing directly —
/// they pre-fill the camelCase fields the orchestrator emits via pydantic
/// <c>by_alias=True</c>.
///
/// Empirical reference shapes:
/// <list type="bullet">
///   <item><c>tests/test_cli_runs.py:246-256</c> — <c>{"kind":"step","data":{"id":..., "stepNumber":..., "nodeName":..., "status":..., "nodeInputs":{...}}}</c></item>
///   <item><c>tests/integration/test_lifecycle_anthropic_mocked.py:239</c> — <c>"kind":"operator_signal"</c> lines</item>
/// </list>
/// </summary>
public sealed record TraceRecord(string Kind, object Data)
{
    /// <summary><c>kind="step"</c> with the <c>StepDto</c> subset DevHub reads.</summary>
    public static TraceRecord Step(string nodeName, string status = "completed", string? taskId = null, int stepNumber = 1)
        => new("step", new
        {
            id = Guid.NewGuid(),
            stepNumber,
            nodeName,
            status,
            nodeInputs = taskId is null ? new object() : new { taskId },
        });

    /// <summary><c>kind="operator_signal"</c> with the <c>RunSignalDto</c> subset DevHub reads.</summary>
    public static TraceRecord OperatorSignal(string name, string? taskId = null, object? payload = null)
        => new("operator_signal", new
        {
            id = Guid.NewGuid(),
            runId = Guid.NewGuid(),
            name,
            taskId,
            payload = payload ?? new { },
            receivedAt = DateTimeOffset.UtcNow,
            dedupeKey = Guid.NewGuid().ToString("N"),
        });
}
