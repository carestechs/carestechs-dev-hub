using DevHub.Contracts.Persistence;
using DevHub.Modules.Identity.Entities.Enums;

namespace DevHub.Modules.Identity.Entities;

public sealed class IdentityCredential : BaseEntity
{
    public required Guid MemberId { get; set; }
    public CredentialProvider Provider { get; set; } = CredentialProvider.Local;
    public string? PasswordHash { get; set; }
    public string? FederatedSubject { get; set; }
}
