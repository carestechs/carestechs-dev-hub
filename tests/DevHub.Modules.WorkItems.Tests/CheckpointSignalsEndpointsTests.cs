using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.WorkItems.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

[Collection("postgres")]
public class CheckpointSignalsEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public CheckpointSignalsEndpointsTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"sig_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();

        // The fake's default scripted approve contract requires `operator` role — but the
        // executor router's contract list is what auth consults. Register a contract for the
        // seeded feature-delivery executor so signal auth works.
        await SeedApproveContractAsync();

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

    private async Task SeedApproveContractAsync()
    {
        var op = await _factory.LoginOperatorAsync();
        // Look up the seeded executor's id.
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
                },
            },
        });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<Guid> StartAsync()
    {
        var dto = await _operator.StartWorkItemAsync(_projectId);
        return dto.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Signal_with_correct_role_returns_200_and_audits()
    {
        var id = await StartAsync();
        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{id}/checkpoints/approve/signal",
            new { outcome = "approve" });
        resp.EnsureSuccessStatusCode();

        var entries = await _factory.AuditEntriesForActionAsync("checkpoint:signal");
        entries.Should().Contain(e => e.Outcome == DevHub.Contracts.Audit.AuditOutcome.Granted);
    }

    [Fact]
    public async Task Signal_as_non_member_returns_403_no_outbound_call()
    {
        var id = await StartAsync();
        _factory.Fake.ResetCalls();

        var (alice, _, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        var resp = await alice.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{id}/checkpoints/approve/signal",
            new { outcome = "approve" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Fake.CountByPath("/signal").Should().Be(0);
    }

    [Fact]
    public async Task Signal_with_outcome_not_in_contract_returns_400_before_forward()
    {
        var id = await StartAsync();
        _factory.Fake.ResetCalls();

        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{id}/checkpoints/approve/signal",
            new { outcome = "banana" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Fake.CountByPath("/signal").Should().Be(0);
    }

    [Fact]
    public async Task Signal_for_unknown_checkpoint_key_returns_404()
    {
        var id = await StartAsync();
        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{id}/checkpoints/no-such/signal",
            new { outcome = "approve" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Signal_with_repeated_idempotency_key_returns_original_response_without_re_forwarding()
    {
        var id = await StartAsync();
        var idem = Guid.NewGuid().ToString("N");

        async Task<HttpResponseMessage> SendAsync()
        {
            var req = new HttpRequestMessage(HttpMethod.Post,
                $"/api/projects/{_projectId}/work-items/{id}/checkpoints/approve/signal");
            req.Headers.Add("Idempotency-Key", idem);
            req.Content = JsonContent.Create(new { outcome = "approve" });
            return await _operator.SendAsync(req);
        }

        var first = await SendAsync();
        first.EnsureSuccessStatusCode();
        var firstSignals = _factory.Fake.CountByPath("/signal");

        var second = await SendAsync();
        second.EnsureSuccessStatusCode();
        // Second call MUST NOT have re-forwarded.
        _factory.Fake.CountByPath("/signal").Should().Be(firstSignals);
    }

    [Fact]
    public async Task Signal_history_returns_paginated_signals()
    {
        var id = await StartAsync();
        await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{id}/checkpoints/approve/signal",
            new { outcome = "approve" });

        var resp = await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{id}/signals?page=1&pageSize=20");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetArrayLength().Should().BeGreaterOrEqualTo(1);
        body.GetProperty("meta").GetProperty("totalCount").GetInt32().Should().BeGreaterOrEqualTo(1);
    }
}
