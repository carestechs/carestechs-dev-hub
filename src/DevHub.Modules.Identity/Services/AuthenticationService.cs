using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Authorization;
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
    IProjectMembershipQuery memberships,
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

        var projectMemberships = await memberships.GetMembershipsAsync(memberId, ct);
        var dtos = projectMemberships
            .Select(m => new MembershipDto(m.ProjectId, m.ProjectSlug, m.Roles))
            .ToList();

        // Workspace-scoped operator grant lives in WorkspaceRoleAssignment, not in any
        // ProjectMembership — surface it explicitly so the SPA can derive isOperator
        // without inspecting the (project-scoped) memberships list.
        var isOperator = await memberships.IsOperatorAsync(memberId, ct);

        return new MeResponse(
            new MemberDto(member.Id, member.DisplayName, member.Email),
            dtos,
            isOperator);
    }

    /// <summary>
    /// Workspace-global + project-scoped role keys for the member. The JWT carries the
    /// union: <c>operator</c> if the member holds the workspace-level operator grant,
    /// plus every role key from any of their project memberships.
    /// </summary>
    private async Task<List<string>> GetRoleKeysAsync(Guid memberId, CancellationToken ct)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        var projectMemberships = await memberships.GetMembershipsAsync(memberId, ct);
        foreach (var membership in projectMemberships)
        {
            foreach (var key in membership.Roles) keys.Add(key);
        }

        if (await memberships.IsOperatorAsync(memberId, ct)) keys.Add("operator");

        return keys.ToList();
    }
}
