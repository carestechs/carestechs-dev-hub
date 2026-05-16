using DevHub.Contracts.Executors;
using DevHub.Contracts.Persistence;
using DevHub.Modules.ExecutorRegistry.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.Modules.ExecutorRegistry;

public static class ExecutorRegistryModuleExtensions
{
    public static IServiceCollection AddExecutorRegistryModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ExecutorRegistryDbContext>((sp, opts) =>
        {
            opts.UseNpgsql(
                    configuration.GetConnectionString("Postgres"),
                    npg => npg.MigrationsHistoryTable("__ef_migrations_history", ExecutorRegistryDbContext.SchemaName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<TimestampingInterceptor>());
        });
        services.AddHostedService<MigrateOnStartup<ExecutorRegistryDbContext>>();

        services.AddScoped<IExecutorRouter, ExecutorRouter>();
        services.AddScoped<IExecutorCredentialResolver, ExecutorCredentialResolver>();
        services.AddScoped<IExecutorRegistrationService, ExecutorRegistrationService>();
        services.AddScoped<IExecutorBindingService, ExecutorBindingService>();

        return services;
    }
}
