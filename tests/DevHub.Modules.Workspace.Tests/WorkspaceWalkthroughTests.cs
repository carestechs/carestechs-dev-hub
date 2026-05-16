using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.Workspace.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

[Collection("postgres")]
public class WorkspaceWalkthroughTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;

    public WorkspaceWalkthroughTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"walk_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory
        {
            ConnectionString = connStr,
            OperatorEmail = "op@test.local",
            OperatorPassword = "OperatorTest123!",
        };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task End_to_end_operator_flow_creates_a_visible_project()
    {
        // 1. Operator logs in.
        var op = await _factory.LoginOperatorAsync();

        // 2. Create a team.
        var team = await op.PostAsJsonAsync("/api/teams", new { name = "Engineering" });
        team.EnsureSuccessStatusCode();
        var teamId = (await team.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // 3. Invite a member.
        var invite = await op.PostAsJsonAsync("/api/members", new
        {
            displayName = "Alice",
            email = "alice@test.local",
        });
        invite.EnsureSuccessStatusCode();
        var aliceId = (await invite.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // 4. Activate Alice (so she can be added to projects) and set her password directly
        //    via the TestHarness helper (no API path ships in v1 for "set password").
        await op.PatchAsJsonAsync($"/api/members/{aliceId}", new { status = "Active" });
        await _factory.SetPasswordAsync(aliceId, "Pw_Alice_123!");

        // 5. Create a project owned by the team.
        var proj = await op.PostAsJsonAsync("/api/projects", new
        {
            name = "Add CSV Export",
            slug = "add-csv-export",
            projectType = "feature-delivery",
            owningTeamId = teamId,
        });
        proj.EnsureSuccessStatusCode();
        var projectId = (await proj.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // 6. Add Alice as a project member with the operator role (only role we ship in v1).
        var addM = await op.PostAsJsonAsync($"/api/projects/{projectId}/memberships", new
        {
            memberId = aliceId,
            roleKeys = new[] { "operator" },
        });
        addM.EnsureSuccessStatusCode();

        // 7. Alice logs in and lists her projects.
        var alice = _factory.CreateClient();
        await alice.LoginAsAsync("alice@test.local", "Pw_Alice_123!");
        var list = await alice.GetAsync("/api/projects");
        list.EnsureSuccessStatusCode();
        var listBody = await list.Content.ReadFromJsonAsync<JsonElement>();
        listBody.GetProperty("data").GetArrayLength().Should().Be(1);
        listBody.GetProperty("data")[0].GetProperty("slug").GetString().Should().Be("add-csv-export");

        // 8. /api/auth/me reflects the membership.
        var me = await alice.GetAsync("/api/auth/me");
        me.EnsureSuccessStatusCode();
        var meBody = await me.Content.ReadFromJsonAsync<JsonElement>();
        var memberships = meBody.GetProperty("data").GetProperty("memberships");
        memberships.GetArrayLength().Should().Be(1);
        memberships[0].GetProperty("projectSlug").GetString().Should().Be("add-csv-export");

        // 9. Audit log carries every Granted action.
        var entries = await _factory.AuditEntriesForActionAsync("team:create");
        entries.Should().Contain(e => e.Reason == "operator");
    }
}

internal static class WalkthroughHttpExtensions
{
    public static async Task<HttpResponseMessage> PatchAsJsonAsync<T>(this HttpClient client, string url, T body)
    {
        var content = JsonContent.Create(body);
        var req = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
        return await client.SendAsync(req);
    }
}
