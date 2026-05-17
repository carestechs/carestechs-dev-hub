using DevHub.Modules.ExecutorRegistry.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.ExecutorRegistry;

public sealed class ExecutorRegistryDbContext(DbContextOptions<ExecutorRegistryDbContext> options) : DbContext(options)
{
    public const string SchemaName = "executor_registry";

    public DbSet<ExecutorRegistration> Executors => Set<ExecutorRegistration>();
    public DbSet<ExecutorBinding> Bindings => Set<ExecutorBinding>();
    public DbSet<CheckpointContract> CheckpointContracts => Set<CheckpointContract>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<ExecutorRegistration>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(60).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
            e.Property(x => x.CredentialsRef).HasMaxLength(120).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Key)
                .IsUnique()
                .HasFilter("\"deleted_at\" IS NULL");
            e.HasMany(x => x.CheckpointContracts)
                .WithOne()
                .HasForeignKey(c => c.ExecutorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExecutorBinding>(e =>
        {
            e.Property(x => x.ProjectType).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.ProjectType)
                .IsUnique()
                .HasFilter("\"deleted_at\" IS NULL");
            e.HasIndex(x => x.ExecutorId);
        });

        modelBuilder.Entity<CheckpointContract>(e =>
        {
            e.Property(x => x.CheckpointKey).HasMaxLength(60).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(x => x.RequiredRoleKey).HasMaxLength(60).IsRequired();
            e.Property(x => x.AllowedOutcomesJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.PerTask).HasDefaultValue(false);
            e.HasIndex(x => new { x.ExecutorId, x.CheckpointKey }).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}
