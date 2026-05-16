using System.ComponentModel.DataAnnotations;

namespace DevHub.Modules.Workspace.DTOs;

public sealed record TeamDto(
    Guid Id,
    string Name,
    string? Description,
    int ProjectCount,
    DateTimeOffset CreatedAt);

public sealed class CreateTeamRequest
{
    [Required, MaxLength(120)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; init; }
}

public sealed class UpdateTeamRequest
{
    [MaxLength(120)]
    public string? Name { get; init; }

    [MaxLength(1000)]
    public string? Description { get; init; }
}
