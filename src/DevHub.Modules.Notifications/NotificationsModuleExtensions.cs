using DevHub.Contracts.Notifications;
using DevHub.Contracts.Persistence;
using DevHub.Modules.Notifications.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.Modules.Notifications;

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>((sp, opts) =>
        {
            opts.UseNpgsql(
                    configuration.GetConnectionString("Postgres"),
                    npg => npg.MigrationsHistoryTable("__ef_migrations_history", NotificationsDbContext.SchemaName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<TimestampingInterceptor>());
        });
        services.AddHostedService<MigrateOnStartup<NotificationsDbContext>>();

        // Singleton: process-wide in-memory channel registry. v1 single-host only.
        services.AddSingleton<PendingActionStreamRegistry>();
        services.AddScoped<IPendingActionReconciler, PendingActionReconciler>();

        return services;
    }
}
