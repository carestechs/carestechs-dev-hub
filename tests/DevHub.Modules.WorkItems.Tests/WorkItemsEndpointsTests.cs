using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.Audit;
using DevHub.Modules.WorkItems.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

[Collection("postgres")]
public class WorkItemsEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public WorkItemsEndpointsTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"wi_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
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
    public async Task Start_as_operator_returns_201_and_audits_granted_with_marker()
    {
        var preCount = _factory.Fake.Total;
        var resp = await _operator.PostAsJsonAsync($"/api/projects/{_projectId}/work-items", new
        {
            title = "Demo work",
            input = new { },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var entries = await _factory.AuditEntriesForActionAsync("workitem:start");
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Granted
            && e.DetailsJson != null
            && e.DetailsJson.Contains("executorCorrelationMarker"));

        _factory.Fake.Total.Should().BeGreaterThan(preCount); // executor was called
    }

    [Fact]
    public async Task Start_as_non_member_returns_403_no_outbound_call()
    {
        _factory.Fake.ResetCalls();
        var (alice, _, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        var resp = await alice.PostAsJsonAsync($"/api/projects/{_projectId}/work-items", new
        {
            title = "Hostile work",
            input = new { },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Fake.Total.Should().Be(0);
    }

    [Fact]
    public async Task Get_as_operator_returns_dto_with_executor_state()
    {
        var dto = await _operator.StartWorkItemAsync(_projectId);
        var id = dto.GetProperty("id").GetGuid();

        var resp = await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{id}");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("title").GetString().Should().Be("Test work");
        body.GetProperty("data").TryGetProperty("executorState", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_as_non_member_returns_403_no_outbound_call()
    {
        var dto = await _operator.StartWorkItemAsync(_projectId);
        var id = dto.GetProperty("id").GetGuid();

        _factory.Fake.ResetCalls();
        var (alice, _, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");

        var resp = await alice.GetAsync($"/api/projects/{_projectId}/work-items/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Fake.Total.Should().Be(0);
    }

    [Fact]
    public async Task List_paginated_returns_envelope_with_meta()
    {
        await _operator.StartWorkItemAsync(_projectId, "First");
        await _operator.StartWorkItemAsync(_projectId, "Second");

        var resp = await _operator.GetAsync($"/api/projects/{_projectId}/work-items?page=1&pageSize=10");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetArrayLength().Should().BeGreaterOrEqualTo(2);
        body.GetProperty("meta").GetProperty("page").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Cancel_as_operator_forwards_and_audits_granted()
    {
        var dto = await _operator.StartWorkItemAsync(_projectId);
        var id = dto.GetProperty("id").GetGuid();

        var resp = await _operator.PostAsync($"/api/projects/{_projectId}/work-items/{id}/cancel", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var entries = await _factory.AuditEntriesForActionAsync("workitem:cancel");
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Granted);
        _factory.Fake.CountByPath("/cancel").Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Cancel_as_non_member_returns_403_no_outbound_call()
    {
        var dto = await _operator.StartWorkItemAsync(_projectId);
        var id = dto.GetProperty("id").GetGuid();

        _factory.Fake.ResetCalls();
        var (alice, _, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");

        var resp = await alice.PostAsync($"/api/projects/{_projectId}/work-items/{id}/cancel", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Fake.Total.Should().Be(0);
    }
}
