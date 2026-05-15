using System.IdentityModel.Tokens.Jwt;
using DevHub.Modules.Identity.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DevHub.Modules.Identity.Tests;

public class JwtTokenIssuerTests
{
    private static readonly JwtIssuerOptions Opts = new()
    {
        Issuer = "https://devhub.test",
        Audience = "devhub-spa",
        SigningKey = "0123456789abcdef0123456789abcdef-min32",
        AccessTokenMinutes = 15,
    };

    private readonly JwtTokenIssuer _sut = new(Options.Create(Opts));

    [Fact]
    public void Issue_EmitsTokenWithSubAndRoleClaims()
    {
        var memberId = Guid.NewGuid();
        var (token, expiresAt) = _sut.Issue(memberId, new[] { "operator", "reviewer" });

        token.Should().NotBeNullOrWhiteSpace();
        expiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Issuer.Should().Be(Opts.Issuer);
        jwt.Audiences.Should().Contain(Opts.Audience);
        jwt.Claims.Should().Contain(c => c.Type == "sub" && c.Value == memberId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "role" && c.Value == "operator");
        jwt.Claims.Should().Contain(c => c.Type == "role" && c.Value == "reviewer");
        jwt.Claims.Should().Contain(c => c.Type == "jti");
    }

    [Fact]
    public void Issue_ExpiresAfterConfiguredMinutes()
    {
        var (token, expiresAt) = _sut.Issue(Guid.NewGuid(), Array.Empty<string>());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var now = DateTimeOffset.UtcNow;
        expiresAt.Should().BeCloseTo(now.AddMinutes(Opts.AccessTokenMinutes), TimeSpan.FromSeconds(5));
        ((DateTimeOffset)jwt.ValidTo).Should().BeCloseTo(expiresAt, TimeSpan.FromSeconds(1));
    }
}
