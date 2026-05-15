namespace DevHub.Contracts.Persistence;

public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
}
