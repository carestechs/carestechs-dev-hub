using System.ComponentModel.DataAnnotations;

namespace DevHub.Modules.Workspace.DTOs;

public sealed record MemberRefDto(Guid Id, string DisplayName, string Email);

public sealed record ProjectMembershipDto(
    Guid Id,
    MemberRefDto Member,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt);

public sealed class AddMembershipRequest
{
    [Required]
    public Guid MemberId { get; init; }

    [Required, MinLength(1)]
    public IReadOnlyList<string> RoleKeys { get; init; } = Array.Empty<string>();
}

public sealed class UpdateMembershipRequest
{
    [Required, MinLength(1)]
    public IReadOnlyList<string> RoleKeys { get; init; } = Array.Empty<string>();
}
