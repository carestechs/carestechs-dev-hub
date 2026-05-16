using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.TestHarness;

/// <summary>
/// WebApplicationFactory wired with sane defaults so integration tests can spin up the API
/// against a Testcontainers Postgres. Override <see cref="ConnectionString"/> per test class
/// to point at the isolated DB returned by <see cref="PostgresFixture.CreateIsolatedDatabaseAsync"/>.
/// </summary>
public sealed class DevHubApiFactory : WebApplicationFactory<Program>
{
    public required string ConnectionString { get; init; }
    public string OperatorPassword { get; init; } = "OperatorTest123!";
    public string OperatorEmail { get; init; } = "op@test.local";

    /// When true (default), seeds a feature-delivery executor + binding before tests run so
    /// existing project-creation paths keep working. Tests covering binding-validation edge
    /// cases should set this to false and seed explicitly.
    public bool SeedFeatureDeliveryBinding { get; init; } = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
                ["Jwt:Issuer"]     = "https://devhub.test",
                ["Jwt:Audience"]   = "devhub-spa",
                ["Jwt:SigningKey"] = "0123456789abcdef0123456789abcdef-min32-test",
                ["Cors:SpaOrigin"] = "http://localhost:4200",
                ["OperatorSeed:Email"]       = OperatorEmail,
                ["OperatorSeed:DisplayName"] = "Operator",
                ["OperatorSeed:Password"]    = OperatorPassword,
            });
        });
        if (SeedFeatureDeliveryBinding)
        {
            builder.ConfigureServices(services =>
            {
                services.AddHostedService<TestRegistrySeeder>();
            });
        }
    }
}
