using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.Identity;
using DevHub.Modules.Identity;
using DevHub.Modules.Identity.Entities;
using DevHub.Modules.Identity.Entities.Enums;
using DevHub.Modules.Identity.Services;
using DevHub.Modules.Workspace;
using DevHub.Modules.Workspace.Entities;
using DevHub.TestHarness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.Modules.Notifications.Tests.Helpers;

internal static class NotificationsTestHelpers
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

    public static async Task SeedApproveContractAsync(this HttpClient @operator, string requiredRoleKey = "operator")
    {
        var list = await @operator.GetAsync("/api/admin/executors");
        var executorId = (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray().First().GetProperty("id").GetGuid();

        var resp = await @operator.PostAsJsonAsync($"/api/admin/executors/{executorId}/checkpoint-contracts", new
        {
            checkpointContracts = new[]
            {
                new
                {
                    checkpointKey = "approve",
                    displayName = "Approve",
                    requiredRoleKey,
                    allowedOutcomes = new[] { "approve", "reject" },
                },
            },
        });
        resp.EnsureSuccessStatusCode();
    }

    public static async Task<Guid> CreateTeamAsync(this HttpClient @operator)
    {
        var resp = await @operator.PostAsJsonAsync("/api/teams", new { name = $"T-{Guid.NewGuid():N}" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateProjectAsync(this HttpClient @operator, Guid teamId)
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
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    public static async Task<Guid> StartWorkItemAsync(this HttpClient client, Guid projectId, string title = "Test")
    {
        var resp = await client.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title,
            input = new { },
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    public static async Task AddMembershipAsync(this HttpClient @operator, Guid projectId, Guid memberId, params string[] roleKeys)
    {
        var resp = await @operator.PostAsJsonAsync($"/api/projects/{projectId}/memberships", new
        {
            memberId,
            roleKeys,
        });
        resp.EnsureSuccessStatusCode();
    }

    public static async Task<JsonElement[]> ListPendingAsync(this HttpClient client)
    {
        var resp = await client.GetAsync("/api/notifications/pending");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").EnumerateArray().ToArray();
    }
}
