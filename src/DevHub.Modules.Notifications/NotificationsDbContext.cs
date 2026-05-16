using DevHub.Modules.Notifications.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Notifications;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string SchemaName = "notifications";

    public DbSet<PendingActionSignal> PendingActionSignals => Set<PendingActionSignal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<PendingActionSignal>(e =>
        {
            e.Property(x => x.CheckpointKey).HasMaxLength(60).IsRequired();
            e.HasIndex(x => new { x.MemberId, x.WorkItemId, x.CheckpointKey })
                .IsUnique()
                .HasFilter("\"dismissed_at\" IS NULL");
            e.HasIndex(x => new { x.MemberId, x.ProjectId })
                .HasFilter("\"dismissed_at\" IS NULL");
        });

        base.OnModelCreating(modelBuilder);
    }
}
