using DevHub.Modules.Audit.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Audit;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string SchemaName = "audit";

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.Property(a => a.TargetType).HasMaxLength(60).IsRequired();
            b.Property(a => a.Action).HasMaxLength(60).IsRequired();
            b.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(a => a.Reason).HasMaxLength(1000);
            b.Property(a => a.DetailsJson).HasColumnType("jsonb");

            // Newest-first lookups dominate the audit log UI.
            b.HasIndex(a => new { a.ProjectId, a.OccurredAt }).IsDescending(false, true);
            b.HasIndex(a => new { a.ActingMemberId, a.OccurredAt }).IsDescending(false, true);
            b.HasIndex(a => new { a.Outcome, a.OccurredAt }).IsDescending(false, true);
        });

        base.OnModelCreating(modelBuilder);
    }
}
