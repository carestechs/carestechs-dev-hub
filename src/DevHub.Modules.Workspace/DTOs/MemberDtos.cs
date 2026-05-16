using System.ComponentModel.DataAnnotations;
using DevHub.Contracts.Identity;

namespace DevHub.Modules.Workspace.DTOs;

public sealed record MemberDto(
    Guid Id,
    string DisplayName,
    string Email,
    MemberStatus Status,
    DateTimeOffset CreatedAt);

public sealed class InviteMemberRequest
{
    [Required, MaxLength(120)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; init; } = string.Empty;
}

public sealed class UpdateMemberRequest
{
    [MaxLength(120)]
    public string? DisplayName { get; init; }

    public MemberStatus? Status { get; init; }
}
