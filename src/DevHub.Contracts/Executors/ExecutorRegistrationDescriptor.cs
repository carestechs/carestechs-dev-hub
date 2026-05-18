namespace DevHub.Contracts.Executors;

public sealed record ExecutorRegistrationDescriptor(
    Guid Id,
    string Key,
    string DisplayName,
    string BaseUrl,
    ExecutorStatus Status,
    IReadOnlyList<CheckpointContractDescriptor> Contracts,
    string Protocol = "devhub");
