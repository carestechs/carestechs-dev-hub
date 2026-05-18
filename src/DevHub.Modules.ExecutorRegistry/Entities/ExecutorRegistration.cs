using DevHub.Contracts.Executors;
using DevHub.Contracts.Persistence;

namespace DevHub.Modules.ExecutorRegistry.Entities;

public sealed class ExecutorRegistration : BaseEntity, ISoftDeletable
{
    public required string Key { get; set; }
    public required string DisplayName { get; set; }
    public required string BaseUrl { get; set; }
    public required string CredentialsRef { get; set; }
    public ExecutorStatus Status { get; set; } = ExecutorStatus.Active;
    /// <summary>
    /// FEAT-010: selects the <c>IExecutorHttpClient</c> implementation. <c>"devhub"</c>
    /// (default; existing protocol) or <c>"orchestrator"</c> (talks to
    /// carestechs-agent-orchestrator's /api/v1/runs).
    /// </summary>
    public string Protocol { get; set; } = "devhub";
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<CheckpointContract> CheckpointContracts { get; set; } = new List<CheckpointContract>();
}
