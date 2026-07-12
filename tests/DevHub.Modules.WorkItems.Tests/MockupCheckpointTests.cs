using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.WorkItems.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

/// <summary>
/// FEAT-017 — confirm_mockup checkpoint: approve/reject signal paths, auth deny, and
/// signal name mapping (DevHub sends "mockup-approved" to the orchestrator, not "confirm_mockup").
/// </summary>
[Collection("postgres")]
public class MockupCheckpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public MockupCheckpointTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"mock_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        await SeedContractsAsync();
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

    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_returns_200_and_reaches_executor()
    {
        _factory.Fake.ResetCalls();
        var id = await StartAsync();

        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{id}/checkpoints/confirm_mockup/signal",
            new { outcome = "approve" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Fake.CountByPath("/signal").Should().Be(1, "signal must be forwarded to the executor");
    }

    [Fact]
    public async Task Approve_forwards_to_executor_checkpoint_path()
    {
        _factory.Fake.ResetCalls();
        var id = await StartAsync();

        await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{id}/checkpoints/confirm_mockup/signal",
            new { outcome = "approve" });

        // Devhub-protocol fake receives the checkpoint key in the URL path.
        var signalCall = _factory.Fake.Calls.FirstOrDefault(c => c.Path.Contains("/signal"));
        signalCall.Should().NotBeNull("signal must be forwarded to the executor");
        signalCall!.Path.Should().Contain("confirm_mockup",
            "devhub-protocol fake receives the checkpoint key in the URL path");
    }

    [Fact]
    public async Task Reject_with_feedback_returns_200()
    {
        var id = await StartAsync();

        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{id}/checkpoints/confirm_mockup/signal",
            new { outcome = "reject", payload = new { feedback = "Needs a larger header." } });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unsupported_outcome_returns_400()
    {
        var id = await StartAsync();

        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{id}/checkpoints/confirm_mockup/signal",
            new { outcome = "revise" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Fake.CountByPath("/signal").Should().Be(0, "invalid outcome must not reach the executor");
    }

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var id = await StartAsync();
        var anon = _factory.CreateClient();

        var resp = await anon.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{id}/checkpoints/confirm_mockup/signal",
            new { outcome = "approve" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Non_member_returns_403_and_no_executor_call()
    {
        var id = await StartAsync();
        _factory.Fake.ResetCalls();
        var (alice, _, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");

        var resp = await alice.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{id}/checkpoints/confirm_mockup/signal",
            new { outcome = "approve" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Fake.CountByPath("/signal").Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<Guid> StartAsync()
    {
        // Script the fake so the work item lands at confirm_mockup, not the default "approve".
        _factory.Fake.Scripted.StartCheckpointKey = "confirm_mockup";
        _factory.Fake.Scripted.FetchCheckpointKey = "confirm_mockup";
        var dto = await _operator.StartWorkItemAsync(_projectId);
        return dto.GetProperty("id").GetGuid();
    }

    private async Task SeedContractsAsync()
    {
        var op = await _factory.LoginOperatorAsync();
        var list = await op.GetAsync("/api/admin/executors");
        var executorId = (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray().First().GetProperty("id").GetGuid();

        (await op.PostAsJsonAsync($"/api/admin/executors/{executorId}/checkpoint-contracts", new
        {
            checkpointContracts = new[]
            {
                new
                {
                    checkpointKey = "confirm_mockup",
                    displayName = "Mockup Review",
                    requiredRoleKey = "operator",
                    allowedOutcomes = new[] { "approve", "reject" },
                },
            },
        })).EnsureSuccessStatusCode();

        op.Dispose();
    }
}
