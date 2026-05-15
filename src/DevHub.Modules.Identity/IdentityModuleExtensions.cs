using DevHub.Contracts.Persistence;
using DevHub.Modules.Identity.Seeding;
using DevHub.Modules.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.Modules.Identity;

public static class IdentityModuleExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>((sp, opts) =>
        {
            opts.UseNpgsql(
                    configuration.GetConnectionString("Postgres"),
                    npg => npg.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<TimestampingInterceptor>());
        });

        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();

        services.AddOptions<IdentitySeedOptions>()
            .Bind(configuration.GetSection(IdentitySeedOptions.SectionName));

        services.AddHostedService<IdentitySeeder>();

        return services;
    }
}
