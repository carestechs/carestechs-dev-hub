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
/// Orchestrator-style trace record. Field names match what
/// <c>OrchestratorExecutorClient</c> reads (see
/// <c>../carestechs-agent-orchestrator/src/app/modules/ai/trace.py</c>).
/// </summary>
public sealed record TraceRecord(
    string Kind,            // "signal" | "step" | etc.
    string? Name = null,    // for signals: "assignment-confirmed", "brief-confirmed", ...
    string? TaskId = null,
    object? Payload = null);
