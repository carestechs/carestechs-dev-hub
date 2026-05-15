using DevHub.TestHarness;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DevHub.Modules.Identity.Tests;

[Collection("postgres")]
public class Migration_AppliesTests(PostgresFixture pg)
{
    [Fact]
    public async Task Initial_migration_applies_and_creates_the_module_schema()
    {
        var connStr = await pg.CreateIsolatedDatabaseAsync($"identity_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connStr)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new IdentityDbContext(options);

        await db.Database.MigrateAsync();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        applied.Should().NotBeEmpty();

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM information_schema.schemata WHERE schema_name = @s";
        cmd.Parameters.AddWithValue("@s", "identity");
        var hit = await cmd.ExecuteScalarAsync();
        hit.Should().NotBeNull($"schema 'identity' should exist after running the module's initial migration");
    }
}
