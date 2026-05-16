using System.ComponentModel.DataAnnotations;
using DevHub.Contracts.Executors;

namespace DevHub.Modules.ExecutorRegistry.DTOs;

public sealed record ExecutorBindingDto(
    Guid Id,
    string ProjectType,
    Guid ExecutorId,
    string ExecutorKey,
    string ExecutorDisplayName,
    ExecutorStatus ExecutorStatus,
    DateTimeOffset CreatedAt);

public sealed class CreateBindingRequest
{
    [Required, MaxLength(60), RegularExpression("^[a-z0-9-]+$")]
    public string ProjectType { get; init; } = string.Empty;

    [Required]
    public Guid ExecutorId { get; init; }
}
