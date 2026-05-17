using DevHub.Modules.WorkItems.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.WorkItems;

public sealed class WorkItemsDbContext(DbContextOptions<WorkItemsDbContext> options) : DbContext(options)
{
    public const string SchemaName = "work_items";

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<CheckpointSignal> CheckpointSignals => Set<CheckpointSignal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<WorkItem>(e =>
        {
            e.Property(x => x.ExecutorCorrelationMarker).HasMaxLength(120).IsRequired();
            e.Property(x => x.Title).HasMaxLength(255).IsRequired();
            e.Property(x => x.CurrentStatus).HasMaxLength(60).IsRequired();
            e.Property(x => x.CurrentCheckpointKey).HasMaxLength(60);
            e.Property(x => x.WorkBranch).HasMaxLength(200);
            e.Property(x => x.CurrentTaskId).HasMaxLength(60);
            e.HasIndex(x => new { x.ExecutorId, x.ExecutorCorrelationMarker }).IsUnique();
            e.HasIndex(x => new { x.ProjectId, x.CurrentStatus });
        });

        modelBuilder.Entity<CheckpointSignal>(e =>
        {
            e.Property(x => x.CheckpointKey).HasMaxLength(60).IsRequired();
            e.Property(x => x.Outcome).HasMaxLength(60).IsRequired();
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.Property(x => x.IdempotencyKey).HasMaxLength(60);
            e.HasIndex(x => new { x.WorkItemId, x.SignaledAt }).IsDescending(false, true);
            e.HasIndex(x => new { x.WorkItemId, x.IdempotencyKey })
                .IsUnique()
                .HasFilter("\"idempotency_key\" IS NOT NULL");
        });

        base.OnModelCreating(modelBuilder);
    }
}
