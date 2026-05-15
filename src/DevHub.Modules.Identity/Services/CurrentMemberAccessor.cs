using System.Security.Claims;
using DevHub.Contracts.Identity;
using Microsoft.AspNetCore.Http;

namespace DevHub.Modules.Identity.Services;

internal sealed class CurrentMemberAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentMember
{
    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public Guid MemberId
    {
        get
        {
            var sub = httpContextAccessor.HttpContext?.User?.FindFirstValue(JwtClaims.MemberId)
                      ?? throw new InvalidOperationException("No authenticated member on the current request.");
            return Guid.Parse(sub);
        }
    }
}

internal static class JwtClaims
{
    /// Claim name we use for the member id. We disable inbound-claim mapping so
    /// the raw JWT `sub` reaches us untouched; `sub` is the canonical claim name.
    public const string MemberId = "sub";
}
