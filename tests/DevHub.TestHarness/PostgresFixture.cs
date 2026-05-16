using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace DevHub.TestHarness;

/// <summary>
/// Boots a single Postgres container for the test session. Test classes that need a DB
/// call <see cref="CreateIsolatedDatabaseAsync"/> to get a private database — fast (no
/// fresh container per test) and isolated (no cross-test interference on the same tables).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithUsername("devhub_test")
        .WithPassword("devhub_test")
        .WithDatabase("devhub_test")
        .Build();

    public string AdminConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a fresh database and returns a connection string scoped to it.</summary>
    public async Task<string> CreateIsolatedDatabaseAsync(string name)
    {
        // Postgres identifiers must start with a letter; UUIDs may start with a digit.
        var safeName = name.Replace('-', '_').ToLowerInvariant();
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{safeName}\"";
        await cmd.ExecuteNonQueryAsync();

        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = safeName };
        return builder.ToString();
    }
}
