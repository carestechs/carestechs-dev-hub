using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Identity;
using DevHub.Modules.Identity.DTOs;
using DevHub.Modules.Identity.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Identity.Services;

public interface IAuthenticationService
{
    Task<LoginOutcome> LoginAsync(string email, string password, CancellationToken cancellationToken);
    Task<RefreshOutcome> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken);
    Task LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken);
    Task<MeResponse> GetCurrentMemberAsync(Guid memberId, CancellationToken cancellationToken);
}

public sealed record LoginOutcome(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshTokenRaw, DateTimeOffset RefreshExpiresAt, MemberDto Member);
public sealed record RefreshOutcome(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshTokenRaw, DateTimeOffset RefreshExpiresAt);

internal sealed class AuthenticationService(
    IdentityDbContext db,
    IMemberLookup members,
    IPasswordHasher hasher,
    IJwtTokenIssuer jwt,
    IRefreshTokenStore refreshStore) : IAuthenticationService
{
    public async Task<LoginOutcome> LoginAsync(string email, string password, CancellationToken ct)
    {
        var member = await members.FindByEmailAsync(email, ct)
                     ?? throw new UnauthorizedException("Invalid email or password.");

        if (member.Status == MemberStatus.Suspended)
            throw new ForbiddenException("Member is suspended.");
        if (member.Status == MemberStatus.Invited)
            throw new ForbiddenException("Member must complete onboarding before signing in.");

        var credential = await db.Credentials.FirstOrDefaultAsync(c => c.MemberId == member.Id, ct);
        if (credential is null || credential.Provider != CredentialProvider.Local || credential.PasswordHash is null)
            throw new UnauthorizedException("Invalid email or password.");

        if (!hasher.Verify(password, credential.PasswordHash))
            throw new UnauthorizedException("Invalid email or password.");

        var roleKeys = await GetRoleKeysAsync(member.Id, ct);
        var (access, accessExp) = jwt.Issue(member.Id, roleKeys);
        var refresh = await refreshStore.IssueAsync(member.Id, ct);

        return new LoginOutcome(
            access, accessExp,
            refresh.RawToken, refresh.ExpiresAt,
            new MemberDto(member.Id, member.DisplayName, member.Email));
    }

    public async Task<RefreshOutcome> RefreshAsync(string rawRefreshToken, CancellationToken ct)
    {
        var rotated = await refreshStore.RotateAsync(rawRefreshToken, ct)
                      ?? throw new UnauthorizedException("Refresh token is invalid or expired.");

        var roleKeys = await GetRoleKeysAsync(rotated.MemberId, ct);
        var (access, accessExp) = jwt.Issue(rotated.MemberId, roleKeys);
        return new RefreshOutcome(access, accessExp, rotated.NewRawToken, rotated.NewExpiresAt);
    }

    public Task LogoutAsync(string rawRefreshToken, CancellationToken ct) =>
        refreshStore.RevokeChainAsync(rawRefreshToken, ct);

    public async Task<MeResponse> GetCurrentMemberAsync(Guid memberId, CancellationToken ct)
    {
        var member = await members.FindByIdAsync(memberId, ct)
                     ?? throw new NotFoundException("Member not found.");
        // Memberships land in FEAT-002 — return empty for the v1 skeleton.
        return new MeResponse(
            new MemberDto(member.Id, member.DisplayName, member.Email),
            Array.Empty<MembershipDto>());
    }

    /// Returns workspace-global role keys for the member (operator etc.). For now
    /// every seeded operator implicitly has the "operator" key; project-scoped
    /// role assignments are FEAT-002. We keep this seam here so the JWT carries
    /// roles end-to-end from day one.
    private Task<List<string>> GetRoleKeysAsync(Guid memberId, CancellationToken ct)
    {
        // No project memberships yet → no project-scoped roles. The seed operator
        // is special-cased here so the JWT issued at first boot includes "operator".
        // FEAT-002 replaces this with a real lookup against RoleAssignments.
        _ = memberId; _ = ct;
        return Task.FromResult(new List<string> { "operator" });
    }
}
