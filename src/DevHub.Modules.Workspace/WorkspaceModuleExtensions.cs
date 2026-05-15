using DevHub.Contracts.Identity;
using DevHub.Contracts.Persistence;
using DevHub.Modules.Workspace.Seeding;
using DevHub.Modules.Workspace.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.Modules.Workspace;

public static class WorkspaceModuleExtensions
{
    public static IServiceCollection AddWorkspaceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<WorkspaceDbContext>((sp, opts) =>
        {
            opts.UseNpgsql(
                    configuration.GetConnectionString("Postgres"),
                    npg => npg.MigrationsHistoryTable("__ef_migrations_history", WorkspaceDbContext.SchemaName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<TimestampingInterceptor>());
        });

        services.AddScoped<IMemberLookup, MemberLookup>();

        services.AddOptions<WorkspaceSeedOptions>()
            .Bind(configuration.GetSection(WorkspaceSeedOptions.SectionName));

        services.AddHostedService<WorkspaceSeeder>();

        return services;
    }
}
