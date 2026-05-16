using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
    }
}
