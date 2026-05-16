using DevHub.TestHarness;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DevHub.Modules.WorkItems.Tests;

[Collection("postgres")]
public class Migration_AppliesTests(PostgresFixture pg)
{
    [Fact]
    public async Task Initial_migration_applies_and_creates_the_module_schema()
    {
        var connStr = await pg.CreateIsolatedDatabaseAsync($"work_items_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<WorkItemsDbContext>()
            .UseNpgsql(connStr)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new WorkItemsDbContext(options);

        await db.Database.MigrateAsync();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        applied.Should().NotBeEmpty();

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM information_schema.schemata WHERE schema_name = @s";
        cmd.Parameters.AddWithValue("@s", "work_items");
        var hit = await cmd.ExecuteScalarAsync();
        hit.Should().NotBeNull($"schema 'work_items' should exist after running the module's initial migration");
    }
}
