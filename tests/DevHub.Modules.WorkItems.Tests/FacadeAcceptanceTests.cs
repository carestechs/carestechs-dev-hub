using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.Audit;
using DevHub.Modules.WorkItems.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

/// FEAT-004 acceptance criteria pegged to specific tests.
[Collection("postgres")]
public class FacadeAcceptanceTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;
    private Guid _workItemId;

    public FacadeAcceptanceTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"ac_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();

        var op = await _factory.LoginOperatorAsync();
        // Seed the approve contract so signal auth works against the fake.
        var list = await op.GetAsync("/api/admin/executors");
        var executorId = (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray().First().GetProperty("id").GetGuid();
        (await op.PostAsJsonAsync($"/api/admin/executors/{executorId}/checkpoint-contracts", new
        {
            checkpointContracts = new[]
            {
                new
                {
                    checkpointKey = "approve",
                    displayName = "Approve",
                    requiredRoleKey = "operator",
                    allowedOutcomes = new[] { "approve", "reject" },
                },
            },
        })).EnsureSuccessStatusCode();

        _operator = op;
        var teamId = await _operator.CreateTeamAsync();
        (_projectId, _) = await _operator.CreateProjectAsync(teamId);
        var dto = await _operator.StartWorkItemAsync(_projectId);
        _workItemId = dto.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AC1_deny_path_never_reaches_executor_across_every_endpoint()
    {
        _factory.Fake.ResetCalls();
        var (alice, _, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");

        // Sweep every façade endpoint as a fresh non-member.
        (await alice.GetAsync($"/api/projects/{_projectId}/work-items"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.GetAsync($"/api/projects/{_projectId}/work-items/{_workItemId}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.PostAsJsonAsync($"/api/projects/{_projectId}/work-items", new { title = "x", input = new { } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{_workItemId}/checkpoints/approve/signal",
            new { outcome = "approve" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.GetAsync($"/api/projects/{_projectId}/work-items/{_workItemId}/signals"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.GetAsync($"/api/projects/{_projectId}/work-items/{_workItemId}/stream",
            HttpCompletionOption.ResponseHeadersRead))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.PostAsync($"/api/projects/{_projectId}/work-items/{_workItemId}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _factory.Fake.Total.Should().Be(0, "no façade deny should ever reach the executor");
    }

    [Fact]
    public async Task AC3_granted_audits_carry_executor_correlation_marker_and_are_consistent()
    {
        // Trigger start (during init) + signal + cancel.
        await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{_workItemId}/checkpoints/approve/signal",
            new { outcome = "approve" });

        // Create a second work item to cancel (the first is now Completed after the signal).
        var second = await _operator.StartWorkItemAsync(_projectId, "Second");
        var secondId = second.GetProperty("id").GetGuid();
        await _operator.PostAsync($"/api/projects/{_projectId}/work-items/{secondId}/cancel", null);

        var starts = await _factory.AuditEntriesForActionAsync("workitem:start");
        var signals = await _factory.AuditEntriesForActionAsync("checkpoint:signal");
        var cancels = await _factory.AuditEntriesForActionAsync("workitem:cancel");

        // Each action produces two Granted rows: the authorization grant (from
        // EnsureAuthorizedAsync, workspace-scope) and the data-target row written by the service.
        // The executorCorrelationMarker lives on the service row.
        starts.Should().Contain(e => e.Outcome == AuditOutcome.Granted
            && e.DetailsJson != null && e.DetailsJson.Contains("executorCorrelationMarker"));
        signals.Should().Contain(e => e.Outcome == AuditOutcome.Granted
            && e.DetailsJson != null && e.DetailsJson.Contains("executorCorrelationMarker"));
        cancels.Should().Contain(e => e.Outcome == AuditOutcome.Granted
            && e.DetailsJson != null && e.DetailsJson.Contains("executorCorrelationMarker"));
    }

    [Fact]
    public async Task AC6_signal_with_invalid_outcome_returns_400_before_forward()
    {
        _factory.Fake.ResetCalls();
        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{_workItemId}/checkpoints/approve/signal",
            new { outcome = "banana" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Fake.CountByPath("/signal").Should().Be(0);
    }
}
