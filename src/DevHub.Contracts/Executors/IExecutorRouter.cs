namespace DevHub.Contracts.Executors;

/// <summary>
/// Cross-module resolver from <c>(projectId | projectType)</c> to the bound executor.
/// FEAT-004 consumes this to route Work Items operations. Returns descriptors only —
/// never entities, never resolved credentials.
/// </summary>
public interface IExecutorRouter
{
    /// Returns the bound executor for a project, or <c>null</c> when the project has no active binding.
    /// Honors executor status but does not filter — the caller decides whether to forward.
    Task<ExecutorRegistrationDescriptor?> ResolveAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// True when an active <c>ExecutorBinding</c> exists for the given <c>projectType</c>.
    Task<bool> IsProjectTypeBoundAsync(string projectType, CancellationToken cancellationToken = default);

    /// Returns the contract for a checkpoint key on a registered executor; the single source of
    /// truth for the <c>requiredRoleKey</c> consumed by FEAT-004's checkpoint authorization.
    Task<CheckpointContractDescriptor?> GetCheckpointContractAsync(
        Guid executorId, string checkpointKey, CancellationToken cancellationToken = default);
}
