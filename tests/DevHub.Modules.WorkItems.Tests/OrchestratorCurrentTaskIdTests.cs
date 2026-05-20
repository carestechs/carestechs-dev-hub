using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.WorkItems.Tests.Helpers;
using DevHub.TestHarness;
using DevHub.TestHarness.FakeOrchestrator;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

/// <summary>
/// BUG-001 / T-096: regression test for the defining scenario in
/// <c>docs/work-items/BUG-001-orchestrator-current-task-id.md</c> §2 — orchestrator
/// paused on <c>confirm_assignment</c> for task <c>T-001</c>, then for <c>T-002</c>,
/// with NO signals delivered. The pre-fix code path returned <c>currentTaskId = null</c>
/// because it scanned incoming-signal records only. The realigned helper reads
/// <c>data.nodeInputs.taskId</c> from the latest <c>step</c>-kind trace record, which
/// the orchestrator's deterministic runtime injects from <c>LifecycleMemory.current_task_id</c>
/// at dispatch time.
/// </summary>
[Collection("postgres")]
public class OrchestratorCurrentTaskIdTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public OrchestratorCurrentTaskIdTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"bug001_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeOrchestrator = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();
        var teamId = await _operator.CreateTeamAsync();
        (_projectId, _) = await _operator.CreateProjectAsync(teamId);
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Get_work_item_currentTaskId_reflects_latest_step_nodeInputs_taskId_with_no_signals()
    {
        // Start the work item — orchestrator transitions to "running" by default.
        var workItemId = (await _operator.StartWorkItemAsync(_projectId))
            .GetProperty("id").GetGuid();

        // Simulate the orchestrator pausing on confirm_assignment for task T-001.
        // CRITICAL for the regression: NO operator_signal records in the trace —
        // the old LatestSignalTaskId fallback would return null here.
        _factory.FakeOrchestrator.Scripted.CurrentRunStatus = "paused";
        _factory.FakeOrchestrator.Scripted.LastStep = new LastStepDto(
            Guid.NewGuid(), 5, "confirm_assignment", "dispatched");
        _factory.FakeOrchestrator.Scripted.TraceRecords.Add(
            TraceRecord.Step("confirm_assignment", taskId: "T-001"));

        var get1 = await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{workItemId}");
        var body1 = (await get1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        body1.GetProperty("currentStatus").GetString().Should().Be("WaitingOnCheckpoint");
        body1.GetProperty("currentCheckpointKey").GetString().Should().Be("assignment-confirmed");
        body1.GetProperty("currentTaskId").GetString().Should().Be("T-001",
            "BUG-001: currentTaskId must come from the latest step's nodeInputs.taskId, not from incoming signals");

        // Now simulate the orchestrator advancing to T-002 (still no signals — the
        // orchestrator's runtime moves on internally and dispatches the next per-task
        // step). DevHub re-fetches and the latest step's taskId wins.
        _factory.FakeOrchestrator.Scripted.TraceRecords.Add(
            TraceRecord.Step("confirm_assignment", taskId: "T-002", stepNumber: 6));

        var get2 = await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{workItemId}");
        var body2 = (await get2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        body2.GetProperty("currentTaskId").GetString().Should().Be("T-002",
            "subsequent step dispatches must replace the prior currentTaskId");
    }
}
