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
using DevHub.TestHarness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.Modules.Workspace.Tests.Helpers;

internal static class WorkspaceTestHelpers
{
    public static async Task<HttpClient> LoginOperatorAsync(this DevHubApiFactory factory, HttpClient? client = null)
    {
        client ??= factory.CreateClient(new() { AllowAutoRedirect = false });
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

    /// <summary>
    /// Seeds a non-operator member with a local credential directly through the DbContexts
    /// (bypasses the API because v1 doesn't ship a "set password" flow). Returns an authenticated
    /// HttpClient + the new member's id.
    /// </summary>
    public static async Task<(HttpClient client, Guid memberId)> LoginFreshMemberAsync(
        this DevHubApiFactory factory,
        string email,
        string password,
        string displayName,
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
        await client.LoginAsAsync(email, password);
        return (client, memberId);
    }

    public static async Task LoginAsAsync(this HttpClient client, string email, string password)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public static async Task<AuditEntry[]> AuditEntriesForActionAsync(this DevHubApiFactory factory, string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await db.AuditEntries.Where(a => a.Action == action).OrderBy(a => a.OccurredAt).ToArrayAsync();
    }

    public static async Task<TWorkspaceMember?> GetMemberByEmailAsync<TWorkspaceMember>(
        this DevHubApiFactory factory, string email) where TWorkspaceMember : class
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ws = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();
        return await ws.Members.FirstOrDefaultAsync(m => m.Email == email) as TWorkspaceMember;
    }

    /// Seeds or updates a Local credential for an existing member. v1 doesn't ship a
    /// "set password" API; tests reach through the DbContext directly.
    public static async Task SetPasswordAsync(this DevHubApiFactory factory, Guid memberId, string password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var id = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var cred = await id.Credentials.FirstOrDefaultAsync(c => c.MemberId == memberId);
        if (cred is null)
        {
            id.Credentials.Add(new IdentityCredential
            {
                MemberId = memberId,
                Provider = CredentialProvider.Local,
                PasswordHash = hasher.Hash(password),
            });
        }
        else
        {
            cred.PasswordHash = hasher.Hash(password);
        }
        await id.SaveChangesAsync();
    }
}
