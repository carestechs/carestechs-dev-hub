using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DevHub.Modules.Identity.Services;

public interface IJwtTokenIssuer
{
    (string Token, DateTimeOffset ExpiresAt) Issue(Guid memberId, IEnumerable<string> roleKeys);
}

public sealed class JwtTokenIssuer(IOptions<JwtIssuerOptions> options) : IJwtTokenIssuer
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid memberId, IEnumerable<string> roleKeys)
    {
        var opts = options.Value;
        var now = DateTimeOffset.UtcNow;
        var exp = now.AddMinutes(opts.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new("sub", memberId.ToString()),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(roleKeys.Select(r => new Claim("role", r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: exp.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), exp);
    }
}

/// <summary>
/// Mirror of Api's JwtOptions but lives in the module so JwtTokenIssuer
/// doesn't reference the host. Bound to the same "Jwt" section.
/// </summary>
public sealed class JwtIssuerOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 15;
}
