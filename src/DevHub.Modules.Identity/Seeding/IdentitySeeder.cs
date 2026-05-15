using DevHub.Contracts.Identity;
using DevHub.Modules.Identity.Entities;
using DevHub.Modules.Identity.Entities.Enums;
using DevHub.Modules.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevHub.Modules.Identity.Seeding;

/// <summary>
/// Hosted service. On startup: applies the Identity migrations, then idempotently
/// ensures the seed operator member has a Local credential with the configured
/// password. Runs after WorkspaceSeeder so the seed member exists.
/// </summary>
public sealed class IdentitySeeder(
    IServiceProvider services,
    IOptions<IdentitySeedOptions> seed,
    ILogger<IdentitySeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var members = scope.ServiceProvider.GetRequiredService<IMemberLookup>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        await db.Database.MigrateAsync(ct);

        var member = await members.FindByEmailAsync(seed.Value.Email, ct);
        if (member is null)
        {
            logger.LogWarning("IdentitySeeder: seed member {Email} not found — Workspace seed didn't run?", seed.Value.Email);
            return;
        }

        var existing = await db.Credentials
            .FirstOrDefaultAsync(c => c.MemberId == member.Id, ct);
        if (existing is null)
        {
            db.Credentials.Add(new IdentityCredential
            {
                MemberId = member.Id,
                Provider = CredentialProvider.Local,
                PasswordHash = hasher.Hash(seed.Value.Password),
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded Local credential for operator {Email}", seed.Value.Email);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class IdentitySeedOptions
{
    public const string SectionName = "OperatorSeed";

    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
