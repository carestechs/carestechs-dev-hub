using DevHub.Contracts.Executors;
using DevHub.Modules.ExecutorRegistry;
using DevHub.Modules.ExecutorRegistry.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DevHub.TestHarness;

/// <summary>
/// Test-time hosted service that ensures a default <c>feature-delivery</c> executor + binding
/// exists. Most workspace/identity tests create projects of type <c>feature-delivery</c>; without
/// this seed those tests would 409 since FEAT-003's ProjectService now validates the binding.
/// </summary>
public sealed class TestRegistrySeeder(
    IServiceScopeFactory scopes,
    string? baseUrlOverride = null,
    string protocol = "devhub") : IHostedService
{
    public const string ExecutorKey = "feature-delivery-v1";
    public const string ProjectType = "feature-delivery";
    private const string DefaultBaseUrl = "http://localhost:9999";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutorRegistryDbContext>();

        if (await db.Bindings.AnyAsync(b => b.ProjectType == ProjectType && b.DeletedAt == null, cancellationToken))
            return;

        var executor = await db.Executors.FirstOrDefaultAsync(e => e.Key == ExecutorKey, cancellationToken);
        if (executor is null)
        {
            executor = new ExecutorRegistration
            {
                Key = ExecutorKey,
                DisplayName = "Feature Delivery v1",
                BaseUrl = baseUrlOverride ?? DefaultBaseUrl,
                CredentialsRef = "TEST_EXEC_TOKEN",
                Status = ExecutorStatus.Active,
                Protocol = protocol,
            };
            db.Executors.Add(executor);
        }

        db.Bindings.Add(new ExecutorBinding
        {
            ProjectType = ProjectType,
            ExecutorId = executor.Id,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
