using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.Identity;
using DevHub.Modules.Audit;
using DevHub.Modules.Audit.Entities;
using DevHub.Modules.Identity;
using DevHub.Modules.Identity.Entities;
using DevHub.Modules.Identity.Entities.Enums;
using DevHub.Modules.Identity.Services;
using DevHub.Modules.Workspace;
using DevHub.Modules.Workspace.Entities;
using DevHub.TestHarness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.Modules.WorkItems.Tests.Helpers;

internal static class WorkItemsTestHelpers
{
    public static async Task<HttpClient> LoginOperatorAsync(this DevHubApiFactory factory)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var resp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = factory.OperatorEmail,
            password = factory.OperatorPassword,
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<(HttpClient client, Guid memberId, string accessToken)> LoginFreshMemberAsync(
        this DevHubApiFactory factory, string email, string password, string displayName,
        MemberStatus status = MemberStatus.Active)
    {
        Guid memberId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var ws = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();
            var id = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            var member = new Member { DisplayName = displayName, Email = email, Status = status };
            ws.Members.Add(member);
            await ws.SaveChangesAsync();

            id.Credentials.Add(new IdentityCredential
            {
                MemberId = member.Id,
                Provider = CredentialProvider.Local,
                PasswordHash = hasher.Hash(password),
            });
            await id.SaveChangesAsync();
            memberId = member.Id;
        }

        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, memberId, token);
    }

    public static async Task<Guid> CreateTeamAsync(this HttpClient @operator)
    {
        var resp = await @operator.PostAsJsonAsync("/api/teams", new { name = $"T-{Guid.NewGuid():N}" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    public static async Task<(Guid id, string slug)> CreateProjectAsync(this HttpClient @operator, Guid teamId)
    {
        var slug = $"p-{Guid.NewGuid():N}".Substring(0, 14);
        var resp = await @operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"P-{Guid.NewGuid():N}",
            slug,
            projectType = "feature-delivery",
            owningTeamId = teamId,
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("data").GetProperty("id").GetGuid(), slug);
    }

    public static async Task<JsonElement> StartWorkItemAsync(
        this HttpClient client, Guid projectId, string title = "Test work")
    {
        var resp = await client.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title,
            input = new { },
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data");
    }

    public static async Task<AuditEntry[]> AuditEntriesForActionAsync(this DevHubApiFactory factory, string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await db.AuditEntries.Where(a => a.Action == action).OrderBy(a => a.OccurredAt).ToArrayAsync();
    }
}
