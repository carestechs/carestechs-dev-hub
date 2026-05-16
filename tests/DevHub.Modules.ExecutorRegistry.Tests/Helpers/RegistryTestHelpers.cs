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
using DevHub.Modules.Workspace.Entities;
using DevHub.Modules.Workspace;
using DevHub.TestHarness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.Modules.ExecutorRegistry.Tests.Helpers;

internal static class RegistryTestHelpers
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

    public static async Task<(HttpClient client, Guid memberId)> LoginFreshMemberAsync(
        this DevHubApiFactory factory, string email, string password, string displayName)
    {
        Guid memberId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var ws = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();
            var id = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            var member = new Member { DisplayName = displayName, Email = email, Status = MemberStatus.Active };
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
        return (client, memberId);
    }

    public static async Task<AuditEntry[]> AuditEntriesForActionAsync(this DevHubApiFactory factory, string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await db.AuditEntries.Where(a => a.Action == action).OrderBy(a => a.OccurredAt).ToArrayAsync();
    }
}
