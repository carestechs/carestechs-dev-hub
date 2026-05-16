using System.Net;
using System.Net.Http.Json;
using DevHub.Contracts.Audit;
using DevHub.Modules.Audit.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Audit.Tests;

/// FEAT-006 AC-2: every authorization denial produces a Denied AuditEntry with a reason.
[Collection("postgres")]
public class AuditDenySweepTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;

    public AuditDenySweepTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"ads_{Guid.NewGuid():N}");
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

    private async Task AssertDeniedWithReasonAsync(string action)
    {
        var entries = await _factory.AuditEntriesForActionAsync(action);
        entries.Should().Contain(e =>
            e.Outcome == AuditOutcome.Denied &&
            !string.IsNullOrEmpty(e.Reason),
            $"every denial must audit {action}:Denied with a reason");
    }

    [Fact]
    public async Task Team_create_as_non_operator_audits_denied()
    {
        var (alice, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        var resp = await alice.PostAsJsonAsync("/api/teams", new { name = "X" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertDeniedWithReasonAsync("team:create");
    }

    [Fact]
    public async Task Member_invite_as_non_operator_audits_denied()
    {
        var (alice, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        var resp = await alice.PostAsJsonAsync("/api/members", new
        {
            displayName = "Bob",
            email = $"b-{Guid.NewGuid():N}@test.local",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertDeniedWithReasonAsync("member:invite");
    }

    [Fact]
    public async Task Executor_create_as_non_operator_audits_denied()
    {
        var (alice, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        var resp = await alice.PostAsJsonAsync("/api/admin/executors", new
        {
            key = "x",
            displayName = "X",
            baseUrl = "http://localhost:9000",
            credentialsRef = "X",
            checkpointContracts = Array.Empty<object>(),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertDeniedWithReasonAsync("executor:create");
    }

    [Fact]
    public async Task Project_create_as_non_operator_audits_denied()
    {
        var teamId = await _operator.CreateTeamAsync();
        var (alice, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        var resp = await alice.PostAsJsonAsync("/api/projects", new
        {
            name = "X", slug = "x-x", projectType = "feature-delivery", owningTeamId = teamId,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertDeniedWithReasonAsync("project:create");
    }

    [Fact]
    public async Task Workitem_start_as_non_member_audits_denied()
    {
        var teamId = await _operator.CreateTeamAsync();
        var projectId = await _operator.CreateProjectAsync(teamId);
        var (alice, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");

        var resp = await alice.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title = "X",
            input = new { },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertDeniedWithReasonAsync("workitem:start");
    }

    [Fact]
    public async Task Workitem_read_as_non_member_audits_denied()
    {
        var teamId = await _operator.CreateTeamAsync();
        var projectId = await _operator.CreateProjectAsync(teamId);
        var startResp = await _operator.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title = "Demo",
            input = new { },
        });
        startResp.EnsureSuccessStatusCode();
        var wid = (await startResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var (alice, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        var resp = await alice.GetAsync($"/api/projects/{projectId}/work-items/{wid}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertDeniedWithReasonAsync("workitem:read");
    }
}
