using System.ComponentModel.DataAnnotations;

namespace DevHub.Modules.Identity.DTOs;

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(1), MaxLength(255)]
    public string Password { get; init; } = string.Empty;
}
