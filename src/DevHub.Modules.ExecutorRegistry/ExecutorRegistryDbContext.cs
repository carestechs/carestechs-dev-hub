using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.ExecutorRegistry;

public sealed class ExecutorRegistryDbContext(DbContextOptions<ExecutorRegistryDbContext> options) : DbContext(options)
{
    public const string SchemaName = "executor_registry";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);
    }
}
