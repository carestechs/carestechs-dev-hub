using System.ComponentModel.DataAnnotations;

namespace DevHub.Api.Options;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    [Required, Url]
    public string SpaOrigin { get; init; } = string.Empty;
}
