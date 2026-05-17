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
            e.Property(x => x.TaskId).HasMaxLength(60);
            // FEAT-009 / T-064: active-row uniqueness is per-task. The COALESCE-with-filter
            // expression isn't expressible in HasIndex(...).HasFilter(...), so the unique
            // constraint lives in raw SQL inside the migration. We keep the columns indexed
            // (non-unique) here so EF still tracks the column tuple in its snapshot.
            e.HasIndex(x => new { x.MemberId, x.WorkItemId, x.CheckpointKey });
            e.HasIndex(x => new { x.MemberId, x.ProjectId })
                .HasFilter("\"dismissed_at\" IS NULL");
        });

        base.OnModelCreating(modelBuilder);
    }
}
