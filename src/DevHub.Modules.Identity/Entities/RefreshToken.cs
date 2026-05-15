using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Identity.Entities;

public sealed class RefreshToken : BaseEntity
{
    public required Guid MemberId { get; set; }
    public required string TokenHash { get; set; }
    public required DateTimeOffset IssuedAt { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
}
