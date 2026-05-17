using DevHub.Contracts.Persistence;

namespace DevHub.Modules.ExecutorRegistry.Entities;

// Replace semantics: no DeletedAt. Rewrites are atomic via POST .../checkpoint-contracts.
public sealed class CheckpointContract : BaseEntity
{
    public required Guid ExecutorId { get; set; }
    public required string CheckpointKey { get; set; }
    public required string DisplayName { get; set; }
    public required string RequiredRoleKey { get; set; }
    public required string AllowedOutcomesJson { get; set; }
    public bool PerTask { get; set; }
}
