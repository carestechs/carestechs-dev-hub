using System.Net.Http.Json;
using System.Net;
using DevHub.Modules.Notifications.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Notifications.Tests;

[Collection("postgres")]
public class NotificationsEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public NotificationsEndpointsTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"npe_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();
        await _operator.SeedApproveContractAsync(requiredRoleKey: "operator");
        var teamId = await _operator.CreateTeamAsync();
        _projectId = await _operator.CreateProjectAsync(teamId);
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Pending_as_operator_returns_caller_scoped_rows_with_display_fields()
    {
        var workItemId = await _operator.StartWorkItemAsync(_projectId, "Demo task");

        var pending = await _operator.ListPendingAsync();
        pending.Should().HaveCount(1);
        var first = pending[0];
        first.GetProperty("workItemId").GetGuid().Should().Be(workItemId);
        first.GetProperty("workItemTitle").GetString().Should().Be("Demo task");
        first.GetProperty("checkpointKey").GetString().Should().Be("approve");
        first.GetProperty("checkpointDisplayName").GetString().Should().Be("Approve");
        first.GetProperty("projectId").GetGuid().Should().Be(_projectId);
    }

    [Fact]
    public async Task Pending_for_a_different_member_does_not_leak_rows()
    {
        await _operator.StartWorkItemAsync(_projectId);

        // Fresh non-operator member, not on the project, should see no pending rows.
        var (alice, _, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");

        var pending = await alice.ListPendingAsync();
        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task Pending_excludes_dismissed_after_signal_resolution()
    {
        var workItemId = await _operator.StartWorkItemAsync(_projectId);
        (await _operator.ListPendingAsync()).Should().HaveCount(1);

        // Resolve the checkpoint → reconciler dismisses the row.
        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{workItemId}/checkpoints/approve/signal",
            new { outcome = "approve" });
        resp.EnsureSuccessStatusCode();

        (await _operator.ListPendingAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Pending_anonymous_returns_401()
    {
        var anon = _factory.CreateClient();
        var resp = await anon.GetAsync("/api/notifications/pending");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
