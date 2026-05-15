using DevHub.Contracts.Persistence;
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
        return services;
    }
}
