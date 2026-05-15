namespace DevHub.Modules.Identity.DTOs;

public sealed record MemberDto(Guid Id, string DisplayName, string Email);
public sealed record MembershipDto(Guid ProjectId, string ProjectSlug, IReadOnlyList<string> Roles);
