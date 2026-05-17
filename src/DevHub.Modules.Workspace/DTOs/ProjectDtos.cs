using System.ComponentModel.DataAnnotations;

namespace DevHub.Modules.Workspace.DTOs;

public sealed record TeamRefDto(Guid Id, string Name);

public sealed record ProjectDto(
    Guid Id,
    string Name,
    string Slug,
    string ProjectType,
    TeamRefDto OwningTeam,
    string? Description,
    string? Repo,
    string? DefaultBranch,
    int InFlightWorkItems,
    DateTimeOffset CreatedAt);

public sealed class CreateProjectRequest
{
    [Required, MaxLength(120)]
    public string Name { get; init; } = string.Empty;

    [Required, MaxLength(60), RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Slug must be lowercase letters, digits, and hyphens.")]
    public string Slug { get; init; } = string.Empty;

    [Required, MaxLength(60)]
    public string ProjectType { get; init; } = string.Empty;

    [Required]
    public Guid OwningTeamId { get; init; }

    [MaxLength(1000)]
    public string? Description { get; init; }

    [MaxLength(140)]
    public string? Repo { get; init; }

    [MaxLength(200)]
    public string? DefaultBranch { get; init; }
}

public sealed class UpdateProjectRequest
{
    [MaxLength(120)]
    public string? Name { get; init; }

    [MaxLength(1000)]
    public string? Description { get; init; }

    [MaxLength(60)]
    public string? ProjectType { get; init; }

    [MaxLength(140)]
    public string? Repo { get; init; }

    [MaxLength(200)]
    public string? DefaultBranch { get; init; }
}
