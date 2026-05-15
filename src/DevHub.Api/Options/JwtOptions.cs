using System.ComponentModel.DataAnnotations;

namespace DevHub.Api.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required, MinLength(1)]
    public string Issuer { get; init; } = string.Empty;

    [Required, MinLength(1)]
    public string Audience { get; init; } = string.Empty;

    [Required, MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 bytes (256 bits) for HS256.")]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 60 * 24)]
    public int AccessTokenMinutes { get; init; } = 15;
}
