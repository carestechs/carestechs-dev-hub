using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.Audit;
using DevHub.Modules.WorkItems.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

/// <summary>
/// FEAT-009 / T-073 — WorkItems-side integration tests for the per-task signal path:
/// taskId forwarding, payload.assignee validation, audit invariants. Backstops AC-4 / AC-5
/// / AC-10 of the FEAT-009 brief.
/// </summary>
[Collection("postgres")]
public class AssignmentSignalTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public AssignmentSignalTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"asn_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();

        await SeedContractsAsync();

        _operator = await _factory.LoginOperatorAsync();
        var teamId = await _operator.CreateTeamAsync();
        (_projectId, _) = await _operator.CreateProjectAsync(teamId);

        // The fake executor's default scripted response uses "approve" — switch it to
        // "assignment-confirmed" so the work item parks on the per-task contract.
        _factory.Fake.Scripted.StartCheckpointKey = "assignment-confirmed";
        _factory.Fake.Scripted.FetchCheckpointKey = "assignment-confirmed";
        _factory.Fake.Scripted.CurrentTaskId = "T-001";
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers BOTH a per-task assignment-confirmed contract AND a regular approve
    /// contract on the seeded executor (replace-semantics atomically swap the whole list).
    /// Tests pick which one to drive by setting the FakeExecutor's StartCheckpointKey.
    /// </summary>
    private async Task SeedContractsAsync()
    {
        var op = await _factory.LoginOperatorAsync();
        var list = await op.GetAsync("/api/admin/executors");
        var executorId = (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray().First().GetProperty("id").GetGuid();

        var resp = await op.PostAsJsonAsync($"/api/admin/executors/{executorId}/checkpoint-contracts", new
        {
            checkpointContracts = new[]
            {
                new
                {
                    checkpointKey = "approve",
                    displayName = "Approve",
                    requiredRoleKey = "operator",
                    allowedOutcomes = new[] { "approve", "reject" },
                    perTask = false,
                },
                new
                {
                    checkpointKey = "assignment-confirmed",
                    displayName = "Confirm task assignment",
                    requiredRoleKey = "operator",
                    allowedOutcomes = new[] { "confirmed" },
                    perTask = true,
                },
            },
        });
        resp.EnsureSuccessStatusCode();
        op.Dispose();
    }

    private async Task<Guid> StartAsync()
    {
        var dto = await _operator.StartWorkItemAsync(_projectId);
        return dto.GetProperty("id").GetGuid();
    }

    // ---------- AC-4: signal forward includes taskId + assignee ----------

    [Fact]
    public async Task Signal_with_taskId_and_assignee_forwards_both_to_executor()
    {
        var workItemId = await StartAsync();
        _factory.Fake.ResetCalls();

        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{workItemId}/checkpoints/assignment-confirmed/signal",
            new { outcome = "confirmed", payload = new { assignee = "Alice" }, taskId = "T-001" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Find the most recent signal call recorded on the FakeExecutor.
        var lastSignal = _factory.Fake.Calls
            .Where(c => c.Method == "POST" && c.Path.Contains("/signal"))
            .OrderByDescending(c => c.OccurredAt)
            .First();
        lastSignal.BodyJson.Should().NotBeNull();

        var body = JsonDocument.Parse(lastSignal.BodyJson!).RootElement;
        body.GetProperty("outcome").GetString().Should().Be("confirmed");
        body.GetProperty("taskId").GetString().Should().Be("T-001");
        body.GetProperty("payload").GetProperty("assignee").GetString().Should().Be("Alice");
    }

    // ---------- AC-5: assignee validation deny path ----------

    [Fact]
    public async Task Signal_assignment_confirmed_without_assignee_returns_400_no_executor_call()
    {
        var workItemId = await StartAsync();
        var preCount = _factory.Fake.CountByPath("/signal");

        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{workItemId}/checkpoints/assignment-confirmed/signal",
            new { outcome = "confirmed", payload = new { } });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("type").GetString().Should().Contain("/validation");
        problem.GetProperty("errors").TryGetProperty("payload.assignee", out _).Should().BeTrue();

        _factory.Fake.CountByPath("/signal").Should().Be(preCount,
            "validation short-circuits before any executor call");
    }

    [Fact]
    public async Task Signal_assignment_confirmed_with_whitespace_assignee_returns_400()
    {
        var workItemId = await StartAsync();
        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{workItemId}/checkpoints/assignment-confirmed/signal",
            new { outcome = "confirmed", payload = new { assignee = "   " }, taskId = "T-001" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Signal_non_per_task_checkpoint_does_not_require_assignee()
    {
        // Switch the fake to the regular "approve" path so the work item parks on a
        // non-per-task contract.
        _factory.Fake.Scripted.StartCheckpointKey = "approve";
        _factory.Fake.Scripted.FetchCheckpointKey = "approve";
        _factory.Fake.Scripted.CurrentTaskId = null;

        var workItemId = await StartAsync();
        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{workItemId}/checkpoints/approve/signal",
            new { outcome = "approve" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------- AC-10: audit details ----------

    [Fact]
    public async Task Audit_signal_row_carries_taskId_and_assignee_in_details()
    {
        var workItemId = await StartAsync();

        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{workItemId}/checkpoints/assignment-confirmed/signal",
            new { outcome = "confirmed", payload = new { assignee = "Bob" }, taskId = "T-001" });
        resp.EnsureSuccessStatusCode();

        var entries = await _factory.AuditEntriesForActionAsync("checkpoint:signal");
        entries.Should().Contain(a =>
            a.Outcome == AuditOutcome.Granted
            && a.DetailsJson != null
            && a.DetailsJson.Contains("T-001")
            && a.DetailsJson.Contains("Bob")
            && a.DetailsJson.Contains("assignee"));
    }
}
