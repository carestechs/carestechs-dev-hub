namespace DevHub.Contracts.Executors;

public sealed record CheckpointContractDescriptor(
    string CheckpointKey,
    string DisplayName,
    string RequiredRoleKey,
    IReadOnlyList<string> AllowedOutcomes,
    bool PerTask = false);
