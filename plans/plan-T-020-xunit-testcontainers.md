# Implementation Plan: T-020 — xUnit + Testcontainers fixture and one passing test per module

## Task Reference
- **Task ID:** T-020
- **Type:** Testing
- **Workflow:** standard
- **Complexity:** M
- **Rationale:** FEAT-001 AC-5. Establishes the integration-test pattern (real Postgres via Testcontainers) for every subsequent backend feature.

## Overview
Stand up the test harness: a shared Postgres container started once per test run, with per-test-class isolated databases. Add one passing migration smoke test per module. Set up the Identity integration-test infrastructure consumed by T-007.

## Implementation Steps

### Step 1: Shared test harness project
**File:** `tests/DevHub.TestHarness/DevHub.TestHarness.csproj`
**Action:** Create
Class library with `<TargetFramework>net10.0</TargetFramework>` and `<IsPackable>false</IsPackable>`. PackageReferences: `Testcontainers.PostgreSql`, `xunit`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.Design`. Add to solution.

### Step 2: PostgresFixture
**File:** `tests/DevHub.TestHarness/PostgresFixture.cs`
**Action:** Create
```csharp
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithUsername("devhub_test")
        .WithPassword("devhub_test")
        .Build();

    public string AdminConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();
    public async Task DisposeAsync() => await _container.DisposeAsync().AsTask();

    public async Task<string> CreateIsolatedDatabaseAsync(string name)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{name}\"";
        await cmd.ExecuteNonQueryAsync();
        var b = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = name };
        return b.ToString();
    }
}
```

### Step 3: Collection definition
**File:** `tests/DevHub.TestHarness/PostgresCollection.cs`
**Action:** Create
```csharp
[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
```
Test classes opt in with `[Collection("postgres")]`.

### Step 4: Hook test projects to the harness
**Files:** `tests/DevHub.Modules.<Module>.Tests/*.csproj` (×6)
**Action:** Modify
Add `<ProjectReference Include="..\..\tests\DevHub.TestHarness\DevHub.TestHarness.csproj" />`.

### Step 5: One migration smoke test per module
**Files:** `tests/DevHub.Modules.<Module>.Tests/Migration_Applies.cs` (×6)
**Action:** Create
For each module:
```csharp
[Collection("postgres")]
public class Migration_Applies(PostgresFixture pg)
{
    [Fact]
    public async Task ApplyingMigrations_CreatesSchema()
    {
        var connStr = await pg.CreateIsolatedDatabaseAsync($"workspace_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<WorkspaceDbContext>()
            .UseNpgsql(connStr).UseSnakeCaseNamingConvention().Options;
        await using var db = new WorkspaceDbContext(options);
        await db.Database.MigrateAsync();

        var hasMigration = await db.Database.GetAppliedMigrationsAsync();
        hasMigration.Should().NotBeEmpty();
    }
}
```
(Equivalent class per module with that module's DbContext.)

### Step 6: Argon2 hasher unit test (Identity)
**File:** `tests/DevHub.Modules.Identity.Tests/Argon2PasswordHasherTests.cs`
**Action:** Create
Verify round-trip (`Hash("pwd")` then `Verify("pwd", hash)` is true; `Verify("other", hash)` is false). Benchmark a single hash and `Assert.That(time, Is.LessThan(2.seconds))` as a rough guard against accidental parameter blow-ups.

### Step 7: WebApplicationFactory for Auth integration tests
**File:** `tests/DevHub.TestHarness/DevHubApiFactory.cs`
**Action:** Create
```csharp
public sealed class DevHubApiFactory : WebApplicationFactory<Program>
{
    public string? ConnectionStringOverride { get; init; }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionStringOverride,
                ["Jwt:Issuer"]     = "https://test.local",
                ["Jwt:Audience"]   = "test",
                ["Jwt:SigningKey"] = "test-signing-key-32-bytes-minimum-1234",
                ["Cors:SpaOrigin"] = "http://localhost:4200",
                ["OperatorSeed:Email"]       = "op@test.local",
                ["OperatorSeed:DisplayName"] = "Op",
                ["OperatorSeed:Password"]    = "test-pwd",
            });
        });
    }
}
```
Note: this requires `DevHub.Api` to expose `Program` as a partial class (add `public partial class Program;` at the end of `Program.cs` — already in T-004 plan if `WebApplication` minimal hosting is used).

### Step 8: Verify
**Action:** Verify
`dotnet test` runs. Every module project has at least one passing test; the suite finishes in under ~2 minutes (Testcontainers startup dominates).

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `tests/DevHub.TestHarness/DevHub.TestHarness.csproj` | Create | Shared harness library |
| `tests/DevHub.TestHarness/PostgresFixture.cs` | Create | Single-container fixture |
| `tests/DevHub.TestHarness/PostgresCollection.cs` | Create | xUnit collection wrapper |
| `tests/DevHub.TestHarness/DevHubApiFactory.cs` | Create | `WebApplicationFactory<Program>` for API integration tests |
| `tests/DevHub.Modules.<Module>.Tests/*.csproj` (×6) | Modify | Reference the harness |
| `tests/DevHub.Modules.<Module>.Tests/Migration_Applies.cs` (×6) | Create | One smoke test per module |
| `tests/DevHub.Modules.Identity.Tests/Argon2PasswordHasherTests.cs` | Create | Hasher round-trip |
| `src/DevHub.Api/Program.cs` | Modify | `public partial class Program;` at end (for WAF) |

## Edge Cases & Risks
- **Slow first run** — Testcontainers pulls the Postgres image once; budget ~30s on a fresh machine. Pre-pulling in CI cache is a later optimization.
- **Port conflicts** — Testcontainers chooses a random host port per container; no conflict risk.
- **Parallel test runs** — xUnit collections serialize tests within a collection; using `[Collection("postgres")]` keeps DB operations sequential against the same container while still parallelizing across the test runner's process boundary.
- **Schema cleanup** — each test class creates an isolated DB with a unique name (`{module}_{guid}`); dropped automatically when the container stops.
- **Docker not running in CI** — gate the test job behind `Docker` daemon availability; document.

## Acceptance Verification
- [ ] `dotnet test` runs and passes on a machine with Docker available.
- [ ] Each module test project contributes ≥1 passing test.
- [ ] The Postgres container is started once and shared across the suite (verify via `docker ps` during run, only one `pg_*` container).
- [ ] Argon2 round-trip test passes; a single hash completes in < 2s on developer hardware.
