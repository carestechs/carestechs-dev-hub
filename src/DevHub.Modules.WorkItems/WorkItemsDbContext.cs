using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.WorkItems;

public sealed class WorkItemsDbContext(DbContextOptions<WorkItemsDbContext> options) : DbContext(options)
{
    public const string SchemaName = "work_items";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);
    }
}
