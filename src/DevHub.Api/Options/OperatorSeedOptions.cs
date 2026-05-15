using System.ComponentModel.DataAnnotations;

namespace DevHub.Api.Options;

public sealed class OperatorSeedOptions
{
    public const string SectionName = "OperatorSeed";

    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(1)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, MinLength(1)]
    public string Password { get; init; } = string.Empty;
}
