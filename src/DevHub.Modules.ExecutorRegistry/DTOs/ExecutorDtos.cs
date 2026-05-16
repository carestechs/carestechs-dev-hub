using System.ComponentModel.DataAnnotations;
using DevHub.Contracts.Executors;

namespace DevHub.Modules.ExecutorRegistry.DTOs;

public sealed record ExecutorDto(
    Guid Id,
    string Key,
    string DisplayName,
    string BaseUrl,
    string CredentialsRef,
    ExecutorStatus Status,
    IReadOnlyList<CheckpointContractDto> CheckpointContracts,
    DateTimeOffset CreatedAt);

public sealed record CheckpointContractDto(
    string CheckpointKey,
    string DisplayName,
    string RequiredRoleKey,
    IReadOnlyList<string> AllowedOutcomes);

public sealed class CreateExecutorRequest
{
    [Required, MaxLength(60)]
    public string Key { get; init; } = string.Empty;

    [Required, MaxLength(120)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, MaxLength(500), Url]
    public string BaseUrl { get; init; } = string.Empty;

    [Required, MaxLength(120)]
    public string CredentialsRef { get; init; } = string.Empty;

    public IReadOnlyList<CreateCheckpointContractRequest> CheckpointContracts { get; init; } =
        Array.Empty<CreateCheckpointContractRequest>();
}

public sealed class CreateCheckpointContractRequest
{
    [Required, MaxLength(60)]
    public string CheckpointKey { get; init; } = string.Empty;

    [Required, MaxLength(120)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, MaxLength(60)]
    public string RequiredRoleKey { get; init; } = string.Empty;

    [Required, MinLength(1)]
    public IReadOnlyList<string> AllowedOutcomes { get; init; } = Array.Empty<string>();
}

public sealed class UpdateExecutorRequest
{
    [MaxLength(120)]
    public string? DisplayName { get; init; }

    [MaxLength(500), Url]
    public string? BaseUrl { get; init; }

    [MaxLength(120)]
    public string? CredentialsRef { get; init; }

    public ExecutorStatus? Status { get; init; }
}

public sealed class ReplaceContractsRequest
{
    [Required, MinLength(1)]
    public IReadOnlyList<CreateCheckpointContractRequest> CheckpointContracts { get; init; } =
        Array.Empty<CreateCheckpointContractRequest>();
}
