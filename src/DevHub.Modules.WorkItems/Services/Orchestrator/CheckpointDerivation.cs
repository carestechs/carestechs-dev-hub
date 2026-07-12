namespace DevHub.Modules.WorkItems.Services.Orchestrator;

/// <summary>
/// FEAT-010: derives DevHub's <c>currentCheckpointKey</c> from the orchestrator's
/// <c>RunDetailDto.lastStep.nodeName</c>.
///
/// The orchestrator's API surface doesn't expose "I'm waiting for signal X" directly —
/// it's a binding-time attribute on the <c>HumanExecutor</c> instance (see
/// <c>../carestechs-agent-orchestrator/src/app/modules/ai/executors/bootstrap.py</c>).
/// In lieu of a runtime field, the lifecycle agent definitions follow a stable naming
/// convention that lets us derive the signal name from the node name.
///
/// Conventions, in priority order:
/// 1. <c>confirm_&lt;X&gt;</c> → <c>&lt;X&gt;-confirmed</c> (e.g. <c>confirm_brief</c> → <c>brief-confirmed</c>).
/// 2. <c>wait_for_implementation</c> → <c>implementation-complete</c> (legacy 0.3.0 flow).
/// 3. <c>confirm_review</c> → <c>review-completed</c>.
/// 4. Anything else → null (logged at INFO; the reconciler treats null gracefully).
/// </summary>
internal static class CheckpointDerivation
{
    /// <summary>
    /// Returns the expected signal name for a node, by convention. Null when no
    /// mapping applies.
    /// </summary>
    public static string? SignalForNodeName(string? nodeName)
    {
        if (string.IsNullOrEmpty(nodeName)) return null;

        // Hand-mapped exceptions first. Names match the lifecycle-agent bootstrap
        // registrations (carestechs-agent-orchestrator/src/app/modules/ai/executors/bootstrap.py).
        switch (nodeName)
        {
            case "request_implementation": return "implementation-complete";
            case "human_review_implementation": return "review-completed";
            // Legacy 0.3.0 aliases (kept for back-compat with older agent versions).
            case "wait_for_implementation": return "implementation-complete";
            case "confirm_review": return "review-completed";
            // confirm_mockup does not follow the confirm_<X> → <X>-confirmed pattern;
            // the orchestrator signal name is "mockup-approved" (resolved in OrchestratorExecutorClient).
            case "confirm_mockup": return "confirm_mockup";
        }

        // confirm_<X> → <X>-confirmed (the dominant pattern in lifecycle-agent@0.4.0-manual).
        const string prefix = "confirm_";
        if (nodeName.StartsWith(prefix, StringComparison.Ordinal))
        {
            var suffix = nodeName[prefix.Length..];
            if (suffix.Length > 0)
                return $"{suffix}-confirmed";
        }

        return null;
    }
}
