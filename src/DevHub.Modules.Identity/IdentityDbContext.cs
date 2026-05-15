using DevHub.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Identity;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string SchemaName = "identity";

    public DbSet<IdentityCredential> Credentials => Set<IdentityCredential>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<IdentityCredential>(b =>
        {
            b.Property(c => c.MemberId).IsRequired();
            b.Property(c => c.Provider).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(c => c.PasswordHash).HasMaxLength(255);
            b.Property(c => c.FederatedSubject).HasMaxLength(255);
            // One credential per member in v1 (federation expansion will lift this).
            b.HasIndex(c => c.MemberId).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.Property(t => t.TokenHash).HasMaxLength(255).IsRequired();
            b.HasIndex(t => t.TokenHash).IsUnique();
            b.HasIndex(t => new { t.MemberId, t.ExpiresAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
