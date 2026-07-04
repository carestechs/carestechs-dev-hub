using DevHub.Contracts.Authorization;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Persistence;
using DevHub.Contracts.Workspace;
using DevHub.Modules.Workspace.Options;
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
        services.AddScoped<IProjectMembershipQuery, ProjectMembershipQuery>();
        services.AddScoped<IProjectAuthorizationService, ProjectAuthorizationService>();
        services.AddScoped<IProjectLookup, ProjectLookup>();
        services.AddScoped<IRoleLookup, RoleLookup>();

        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ProjectDocsService>();
        services.AddScoped<IProjectDocsService>(sp => sp.GetRequiredService<ProjectDocsService>());
        services.AddScoped<IProjectDocsQuery>(sp => sp.GetRequiredService<ProjectDocsService>());
        services.AddScoped<IDocTemplateVersionService, DocTemplateVersionService>();

        services.AddOptions<WorkspaceSeedOptions>()
            .Bind(configuration.GetSection(WorkspaceSeedOptions.SectionName));

        services.AddOptions<GitHubOptions>()
            .Bind(configuration.GetSection(GitHubOptions.SectionName));

        services.AddHttpClient(GitHubService.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com");
            client.DefaultRequestHeaders.Add("User-Agent", "DevHub");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        });

        services.AddScoped<IGitHubService, GitHubService>();

        services.AddHostedService<WorkspaceSeeder>();

        return services;
    }
}
