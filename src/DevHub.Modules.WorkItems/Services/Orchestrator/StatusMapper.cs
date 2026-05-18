namespace DevHub.Modules.WorkItems.Services.Orchestrator;

/// <summary>
/// FEAT-010: maps the carestechs-agent-orchestrator's <c>RunStatus</c> enum values to
/// DevHub's <c>CurrentStatus</c> strings.
/// Source: <c>../carestechs-agent-orchestrator/src/app/modules/ai/enums.py</c>.
/// </summary>
internal static class StatusMapper
{
    public static string MapRunStatus(string orchestratorStatus) => orchestratorStatus switch
    {
        "pending" or "running" => "Running",
        "paused" => "WaitingOnCheckpoint",
        "completed" => "Completed",
        "failed" => "Failed",
        "cancelled" => "Cancelled",
        _ => "Running",  // Conservative fallback for forward-compat with new RunStatus values.
    };
}
