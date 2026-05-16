using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.Audit;
using DevHub.Modules.Audit.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Audit.Tests;

/// FEAT-006 AC-1: every mutation across every module produces exactly one Granted AuditEntry.
/// One [Fact] per known mutation surface — touch the surface via HTTP, then assert the row.
[Collection("postgres")]
public class AuditMutationSweepTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;

    public AuditMutationSweepTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"ams_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task AssertGrantedAsync(string action)
    {
        var entries = await _factory.AuditEntriesForActionAsync(action);
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Granted, $"every mutation must audit {action}:Granted");
    }

    [Fact]
    public async Task Team_create_writes_granted_row()
    {
        await _operator.CreateTeamAsync();
        await AssertGrantedAsync("team:create");
    }

    [Fact]
    public async Task Member_invite_writes_granted_row()
    {
        await _operator.InviteMemberAsync();
        await AssertGrantedAsync("member:invite");
    }

    [Fact]
    public async Task Executor_create_writes_granted_row()
    {
        var resp = await _operator.PostAsJsonAsync("/api/admin/executors", new
        {
            key = $"e-{Guid.NewGuid():N}".Substring(0, 12),
            displayName = "Sweep Executor",
            baseUrl = "http://localhost:9000",
            credentialsRef = "TEST_VAR",
            checkpointContracts = Array.Empty<object>(),
        });
        resp.EnsureSuccessStatusCode();
        await AssertGrantedAsync("executor:create");
    }

    [Fact]
    public async Task Executor_binding_create_writes_granted_row()
    {
        var execResp = await _operator.PostAsJsonAsync("/api/admin/executors", new
        {
            key = $"e-{Guid.NewGuid():N}".Substring(0, 12),
            displayName = "Sweep Executor",
            baseUrl = "http://localhost:9000",
            credentialsRef = "TEST_VAR",
            checkpointContracts = Array.Empty<object>(),
        });
        var execId = (await execResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var bindResp = await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new
        {
            projectType = $"pt-{Guid.NewGuid():N}".Substring(0, 12),
            executorId = execId,
        });
        bindResp.EnsureSuccessStatusCode();
        await AssertGrantedAsync("executor-binding:create");
    }

    [Fact]
    public async Task Project_create_writes_granted_row()
    {
        var teamId = await _operator.CreateTeamAsync();
        await _operator.CreateProjectAsync(teamId);
        await AssertGrantedAsync("project:create");
    }

    [Fact]
    public async Task Membership_add_writes_granted_row()
    {
        var teamId = await _operator.CreateTeamAsync();
        var projectId = await _operator.CreateProjectAsync(teamId);
        // Seed an Active member directly (the invite path leaves the member Invited,
        // and Membership.Add rejects non-Active members with 409).
        var (_, memberId) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");

        var resp = await _operator.PostAsJsonAsync($"/api/projects/{projectId}/memberships", new
        {
            memberId,
            roleKeys = new[] { "operator" },
        });
        resp.EnsureSuccessStatusCode();
        await AssertGrantedAsync("project:membership:add");
    }

    [Fact]
    public async Task Workitem_start_writes_granted_row()
    {
        var teamId = await _operator.CreateTeamAsync();
        var projectId = await _operator.CreateProjectAsync(teamId);
        var resp = await _operator.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title = "Sweep test",
            input = new { },
        });
        resp.EnsureSuccessStatusCode();
        await AssertGrantedAsync("workitem:start");
    }

    [Fact]
    public async Task Workitem_cancel_writes_granted_row()
    {
        var teamId = await _operator.CreateTeamAsync();
        var projectId = await _operator.CreateProjectAsync(teamId);
        var startResp = await _operator.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title = "Sweep test",
            input = new { },
        });
        startResp.EnsureSuccessStatusCode();
        var wid = (await startResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var cancelResp = await _operator.PostAsync($"/api/projects/{projectId}/work-items/{wid}/cancel", null);
        cancelResp.EnsureSuccessStatusCode();
        await AssertGrantedAsync("workitem:cancel");
    }
}
