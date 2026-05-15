namespace DevHub.Modules.Identity.DTOs;

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, MemberDto Member);
public sealed record RefreshResponse(string AccessToken, DateTimeOffset ExpiresAt);
public sealed record MeResponse(MemberDto Member, IReadOnlyList<MembershipDto> Memberships);
