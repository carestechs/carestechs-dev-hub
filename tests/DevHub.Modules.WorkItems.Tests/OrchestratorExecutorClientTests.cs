using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.WorkItems.Tests.Helpers;
using DevHub.TestHarness;
using DevHub.TestHarness.FakeOrchestrator;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

/// <summary>
/// FEAT-010 / T-088: integration tests for <c>OrchestratorExecutorClient</c> against the
/// in-process <see cref="FakeOrchestratorHost"/>. Covers status mapping, checkpoint
/// derivation, executor-state assembly, signal forward shape, and run-id persistence.
/// </summary>
[Collection("postgres")]
public class OrchestratorExecutorClientTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public OrchestratorExecutorClientTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"orch_{Guid.NewGuid():N}");
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

    // ---------- AC-2: Start ----------

    [Fact]
    public async Task Start_posts_to_orchestrator_runs_and_persists_runId()
    {
        _factory.FakeOrchestrator.Scripted.CurrentRunStatus = "running";
        _factory.FakeOrchestrator.ResetCalls();

        var resp = await _operator.PostAsJsonAsync($"/api/projects/{_projectId}/work-items", new
        {
            title = "Demo",
            input = new { },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Orchestrator received a POST /api/v1/runs.
        var createCall = _factory.FakeOrchestrator.Calls.Single(c =>
            c.Method == "POST" && c.Path == "/api/v1/runs");
        createCall.BodyJson.Should().NotBeNullOrEmpty();
        var body = JsonDocument.Parse(createCall.BodyJson!).RootElement;
        body.GetProperty("agentRef").GetString().Should().Be("feature-delivery-v1",
            "agentRef defaults to the executor's Key in v1");
        body.GetProperty("intake").GetProperty("workItem").GetProperty("id").GetString().Should().NotBeNullOrEmpty();

        // DevHub persisted the run id back on the WorkItem row.
        var dto = (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data");
        dto.GetProperty("executorRunId").GetGuid().Should().NotBe(Guid.Empty);
        dto.GetProperty("currentStatus").GetString().Should().Be("Running");
    }

    [Fact]
    public async Task Start_forwards_intake_codeSource_from_FEAT_008()
    {
        // Seed project repo + defaultBranch via PATCH.
        var patch = await _operator.PatchAsJsonAsync($"/api/projects/{_projectId}",
            new { repo = "acme/widgets", defaultBranch = "main" });
        patch.EnsureSuccessStatusCode();

        _factory.FakeOrchestrator.ResetCalls();
        await _operator.PostAsJsonAsync($"/api/projects/{_projectId}/work-items", new
        {
            title = "X",
            input = new { },
            workBranch = "feat/abc",
        });

        var createCall = _factory.FakeOrchestrator.Calls.Single(c => c.Path == "/api/v1/runs");
        var body = JsonDocument.Parse(createCall.BodyJson!).RootElement;
        var codeSource = body.GetProperty("intake").GetProperty("codeSource");
        codeSource.GetProperty("repo").GetString().Should().Be("acme/widgets");
        codeSource.GetProperty("baseBranch").GetString().Should().Be("main");
        codeSource.GetProperty("workBranch").GetString().Should().Be("feat/abc");
    }

    // ---------- AC-3: Fetch + status mapping ----------

    [Theory]
    [InlineData("pending", "Running")]
    [InlineData("running", "Running")]
    [InlineData("paused", "WaitingOnCheckpoint")]
    [InlineData("completed", "Completed")]
    [InlineData("failed", "Failed")]
    [InlineData("cancelled", "Cancelled")]
    public async Task Fetch_maps_RunStatus_to_CurrentStatus(string orchStatus, string expected)
    {
        var workItemId = (await _operator.StartWorkItemAsync(_projectId))
            .GetProperty("id").GetGuid();

        _factory.FakeOrchestrator.Scripted.CurrentRunStatus = orchStatus;
        var get = await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{workItemId}");
        get.EnsureSuccessStatusCode();
        var body = (await get.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        body.GetProperty("currentStatus").GetString().Should().Be(expected);
    }

    // ---------- AC-3: currentCheckpointKey derivation by node name ----------

    [Fact]
    public async Task Fetch_derives_assignment_confirmed_from_confirm_assignment_node()
    {
        var workItemId = (await _operator.StartWorkItemAsync(_projectId))
            .GetProperty("id").GetGuid();

        _factory.FakeOrchestrator.Scripted.CurrentRunStatus = "paused";
        _factory.FakeOrchestrator.Scripted.LastStep = new LastStepDto(
            Guid.NewGuid(), 5, "confirm_assignment", "dispatched");

        var get = await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{workItemId}");
        var body = (await get.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        body.GetProperty("currentStatus").GetString().Should().Be("WaitingOnCheckpoint");
        body.GetProperty("currentCheckpointKey").GetString().Should().Be("assignment-confirmed");
    }

    [Fact]
    public async Task Fetch_derives_implementation_complete_from_wait_for_implementation_node()
    {
        var workItemId = (await _operator.StartWorkItemAsync(_projectId))
            .GetProperty("id").GetGuid();
        _factory.FakeOrchestrator.Scripted.CurrentRunStatus = "paused";
        _factory.FakeOrchestrator.Scripted.LastStep = new LastStepDto(
            Guid.NewGuid(), 7, "wait_for_implementation", "dispatched");

        var get = await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{workItemId}");
        var body = (await get.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        body.GetProperty("currentCheckpointKey").GetString().Should().Be("implementation-complete");
    }

    [Fact]
    public async Task Fetch_returns_null_checkpoint_when_node_name_does_not_match_convention()
    {
        var workItemId = (await _operator.StartWorkItemAsync(_projectId))
            .GetProperty("id").GetGuid();
        _factory.FakeOrchestrator.Scripted.CurrentRunStatus = "paused";
        _factory.FakeOrchestrator.Scripted.LastStep = new LastStepDto(
            Guid.NewGuid(), 3, "do_something_obscure", "dispatched");

        var get = await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{workItemId}");
        var body = (await get.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        body.GetProperty("currentCheckpointKey").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---------- AC-3: executorState.assignments replay ----------

    [Fact]
    public async Task Fetch_assembles_assignments_map_from_trace_signals()
    {
        var workItemId = (await _operator.StartWorkItemAsync(_projectId))
            .GetProperty("id").GetGuid();

        // Pre-seed two assignment-confirmed signals + one unrelated signal.
        _factory.FakeOrchestrator.Scripted.TraceRecords.AddRange(new[]
        {
            new TraceRecord("signal", "assignment-confirmed", "T-001", new { assignee = "Alice" }),
            new TraceRecord("signal", "assignment-confirmed", "T-002", new { assignee = "Bob" }),
            new TraceRecord("signal", "tasks-confirmed", "T-001", null),  // ignored
        });

        var get = await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{workItemId}");
        var body = (await get.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var assignments = body.GetProperty("executorState").GetProperty("assignments");
        assignments.GetProperty("T-001").GetString().Should().Be("Alice");
        assignments.GetProperty("T-002").GetString().Should().Be("Bob");
        assignments.EnumerateObject().Count().Should().Be(2);
    }

    // ---------- AC-4: Signal forward shape ----------

    [Fact]
    public async Task Signal_forwards_checkpointKey_as_name_with_taskId_and_payload()
    {
        // Seed a per-task assignment-confirmed contract — this is the production target
        // for FEAT-009 + FEAT-010, so it's the most realistic shape to test.
        await SeedAssignmentConfirmedContractAsync();

        var workItemId = (await _operator.StartWorkItemAsync(_projectId))
            .GetProperty("id").GetGuid();

        // Park the work item on the checkpoint: orchestrator status=paused +
        // last_step.node_name=confirm_assignment → DevHub derives checkpoint=assignment-confirmed.
        // DevHub's pre-flight check (currentStatus=WaitingOnCheckpoint + currentCheckpointKey
        // matches the signal target) then passes.
        _factory.FakeOrchestrator.Scripted.CurrentRunStatus = "paused";
        _factory.FakeOrchestrator.Scripted.LastStep = new LastStepDto(
            Guid.NewGuid(), 3, "confirm_assignment", "dispatched");

        // Force DevHub to refresh its cached state by GETting once (the signal endpoint
        // reads from the cache, set on every GET).
        (await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{workItemId}"))
            .EnsureSuccessStatusCode();
        _factory.FakeOrchestrator.ResetCalls();

        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{workItemId}/checkpoints/assignment-confirmed/signal",
            new { outcome = "confirmed", payload = new { assignee = "Alice" }, taskId = "T-001" });
        resp.EnsureSuccessStatusCode();

        var signalCall = _factory.FakeOrchestrator.Calls.Single(c =>
            c.Method == "POST" && c.Path.Contains("/signals"));
        var body = JsonDocument.Parse(signalCall.BodyJson!).RootElement;
        body.GetProperty("name").GetString().Should().Be("assignment-confirmed");
        body.GetProperty("taskId").GetString().Should().Be("T-001");
        body.GetProperty("payload").GetProperty("assignee").GetString().Should().Be("Alice");
    }

    private async Task SeedAssignmentConfirmedContractAsync()
    {
        var list = await _operator.GetAsync("/api/admin/executors");
        var executorId = (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray().First().GetProperty("id").GetGuid();
        var resp = await _operator.PostAsJsonAsync(
            $"/api/admin/executors/{executorId}/checkpoint-contracts", new
            {
                checkpointContracts = new[]
                {
                    new
                    {
                        checkpointKey = "assignment-confirmed",
                        displayName = "Confirm assignment",
                        requiredRoleKey = "operator",
                        allowedOutcomes = new[] { "confirmed" },
                        perTask = true,
                    },
                },
            });
        resp.EnsureSuccessStatusCode();
    }

    // ---------- AC-6: Cancel ----------

    [Fact]
    public async Task Cancel_posts_to_orchestrator_cancel_endpoint()
    {
        var workItemId = (await _operator.StartWorkItemAsync(_projectId))
            .GetProperty("id").GetGuid();
        _factory.FakeOrchestrator.ResetCalls();

        var resp = await _operator.PostAsync(
            $"/api/projects/{_projectId}/work-items/{workItemId}/cancel", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        _factory.FakeOrchestrator.Calls.Should().ContainSingle(c =>
            c.Method == "POST" && c.Path.EndsWith("/cancel"));
    }

    // ---------- AC-9: Backwards compatibility (covered by suite: existing tests still pass) ----------
    // No explicit test; existing 190-test suite running under UseFakeExecutor = true is the
    // regression net.
}
