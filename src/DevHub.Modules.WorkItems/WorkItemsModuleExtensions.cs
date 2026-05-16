using DevHub.Contracts.Executors;
using DevHub.Contracts.Persistence;
using DevHub.Modules.WorkItems.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.Modules.WorkItems;

public static class WorkItemsModuleExtensions
{
    public static IServiceCollection AddWorkItemsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<WorkItemsDbContext>((sp, opts) =>
        {
            opts.UseNpgsql(
                    configuration.GetConnectionString("Postgres"),
                    npg => npg.MigrationsHistoryTable("__ef_migrations_history", WorkItemsDbContext.SchemaName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<TimestampingInterceptor>());
        });
        services.AddHostedService<MigrateOnStartup<WorkItemsDbContext>>();

        // Typed HttpClient. Timeout applies to non-streaming calls; OpenStreamAsync uses
        // HttpCompletionOption.ResponseHeadersRead so the body stream is independent.
        services.AddHttpClient<IExecutorHttpClient, ExecutorHttpClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IWorkItemsService, WorkItemsService>();
        services.AddScoped<ICheckpointSignalsService, CheckpointSignalsService>();
        services.AddScoped<WorkItemStreamForwarder>();

        return services;
    }
}
