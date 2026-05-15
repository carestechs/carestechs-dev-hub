using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevHub.Contracts.Persistence;

/// <summary>
/// Hosted service that runs pending EF migrations on a specific <typeparamref name="TContext"/>
/// at startup. One registration per DbContext.
/// </summary>
/// <remarks>
/// EF Core advisory-locks the migrations history table so concurrent instances starting at
/// the same time are safe — one applies, the others wait, both see the resulting schema.
/// </remarks>
public sealed class MigrateOnStartup<TContext>(
    IServiceProvider services,
    ILogger<MigrateOnStartup<TContext>> logger) : IHostedService
    where TContext : DbContext
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        logger.LogInformation("Applying migrations for {Context}", typeof(TContext).Name);
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
