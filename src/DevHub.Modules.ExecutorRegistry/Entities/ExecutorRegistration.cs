using DevHub.Contracts.Persistence;
using DevHub.Modules.ExecutorRegistry.Enums;

namespace DevHub.Modules.ExecutorRegistry.Entities;

public sealed class ExecutorRegistration : BaseEntity, ISoftDeletable
{
    public required string Key { get; set; }
    public required string DisplayName { get; set; }
    public required string BaseUrl { get; set; }
    public required string CredentialsRef { get; set; }
    public ExecutorStatus Status { get; set; } = ExecutorStatus.Active;
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<CheckpointContract> CheckpointContracts { get; set; } = new List<CheckpointContract>();
}
