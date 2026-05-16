using DevHub.Contracts.Persistence;

namespace DevHub.Modules.ExecutorRegistry.Entities;

public sealed class ExecutorBinding : BaseEntity, ISoftDeletable
{
    public required string ProjectType { get; set; }
    public required Guid ExecutorId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
