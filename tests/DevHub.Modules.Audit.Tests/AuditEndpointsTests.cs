using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.Audit;
using DevHub.Modules.Audit.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevHub.Modules.Audit.Tests;

[Collection("postgres")]
public class AuditEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;

    public AuditEndpointsTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"aep_{Guid.NewGuid():N}");
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

    private async Task<JsonElement> GetEnvelopeAsync(HttpClient client, string url)
    {
        var resp = await client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Project_audit_as_operator_returns_envelope_with_meta()
    {
        var teamId = await _operator.CreateTeamAsync();
        var projectId = await _operator.CreateProjectAsync(teamId);

        var body = await GetEnvelopeAsync(_operator, $"/api/projects/{projectId}/audit?pageSize=50");
        body.GetProperty("data").GetArrayLength().Should().BeGreaterThan(0);
        body.GetProperty("meta").GetProperty("page").GetInt32().Should().Be(1);
        // All returned rows are scoped to the project.
        foreach (var row in body.GetProperty("data").EnumerateArray())
        {
            row.GetProperty("projectId").GetGuid().Should().Be(projectId);
        }
    }

    [Fact]
    public async Task Project_audit_as_non_member_returns_403_and_audits_denied()
    {
        var teamId = await _operator.CreateTeamAsync();
        var projectId = await _operator.CreateProjectAsync(teamId);
        var (alice, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");

        var resp = await alice.GetAsync($"/api/projects/{projectId}/audit");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var entries = await _factory.AuditEntriesForActionAsync("audit:read");
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Denied);
    }

    [Fact]
    public async Task Admin_audit_as_operator_returns_envelope()
    {
        // Generate at least one row.
        await _operator.CreateTeamAsync();

        var body = await GetEnvelopeAsync(_operator, "/api/admin/audit?pageSize=10");
        body.GetProperty("data").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Admin_audit_as_non_operator_returns_403_and_audits_denied()
    {
        var (alice, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");

        var resp = await alice.GetAsync("/api/admin/audit");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var entries = await _factory.AuditEntriesForActionAsync("audit:read:admin");
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Denied);
    }

    [Fact]
    public async Task Admin_audit_filter_by_outcome_denied_returns_only_denied()
    {
        // Generate at least one denied row first.
        var (alice, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        await alice.PostAsJsonAsync("/api/teams", new { name = "ShouldDeny" });

        var body = await GetEnvelopeAsync(_operator, "/api/admin/audit?outcome=Denied&pageSize=200");
        var rows = body.GetProperty("data").EnumerateArray().ToArray();
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r.GetProperty("outcome").GetString() == "Denied");
    }

    [Fact]
    public async Task Admin_audit_filter_by_action_returns_only_matching()
    {
        await _operator.CreateTeamAsync();
        await _operator.CreateTeamAsync();

        var body = await GetEnvelopeAsync(_operator, "/api/admin/audit?action=team:create&pageSize=200");
        var rows = body.GetProperty("data").EnumerateArray().ToArray();
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r.GetProperty("action").GetString() == "team:create");
    }

    [Fact]
    public async Task Admin_audit_filter_by_acting_member_returns_only_that_member()
    {
        await _operator.CreateTeamAsync(); // operator-acted
        var meResp = await _operator.GetAsync("/api/auth/me");
        meResp.EnsureSuccessStatusCode();
        var meBody = await meResp.Content.ReadFromJsonAsync<JsonElement>();
        var operatorMemberId = meBody.GetProperty("data").GetProperty("member").GetProperty("id").GetGuid();

        var body = await GetEnvelopeAsync(_operator,
            $"/api/admin/audit?actingMemberId={operatorMemberId}&pageSize=200");
        var rows = body.GetProperty("data").EnumerateArray().ToArray();
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r =>
            r.GetProperty("actingMember").GetProperty("id").GetGuid() == operatorMemberId);
    }

    [Fact]
    public async Task Admin_audit_filter_by_from_returns_only_rows_after()
    {
        await _operator.CreateTeamAsync();
        await Task.Delay(50);
        var threshold = DateTimeOffset.UtcNow;
        await Task.Delay(50);
        await _operator.CreateTeamAsync();

        var body = await GetEnvelopeAsync(_operator,
            $"/api/admin/audit?from={Uri.EscapeDataString(threshold.ToString("O"))}&pageSize=100");
        var rows = body.GetProperty("data").EnumerateArray().ToArray();
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r =>
            r.GetProperty("occurredAt").GetDateTimeOffset() >= threshold);
    }

    [Fact]
    public async Task Page_size_clamped_to_max()
    {
        // PageRequest.Normalize() caps PageSize at 100 (global API standard); the audit
        // endpoint inherits the cap. Over-shoots are silently clamped.
        await _operator.CreateTeamAsync();
        var body = await GetEnvelopeAsync(_operator, "/api/admin/audit?pageSize=10000");
        body.GetProperty("meta").GetProperty("pageSize").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task Project_audit_history_preserved_after_project_soft_delete()
    {
        var teamId = await _operator.CreateTeamAsync();
        var projectId = await _operator.CreateProjectAsync(teamId);
        // Generate a few rows on the project.
        var startResp = await _operator.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title = "Demo",
            input = new { },
        });
        startResp.EnsureSuccessStatusCode();

        // Soft-delete the project.
        var delResp = await _operator.DeleteAsync($"/api/projects/{projectId}");
        delResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Audit history for that project is still readable via /admin/audit?projectId=…
        var body = await GetEnvelopeAsync(_operator,
            $"/api/admin/audit?projectId={projectId}&pageSize=200");
        var rows = body.GetProperty("data").EnumerateArray().ToArray();
        rows.Should().NotBeEmpty();
        rows.Should().Contain(r => r.GetProperty("action").GetString() == "workitem:start");
    }

    [Fact]
    public async Task Admin_audit_anonymous_returns_401()
    {
        var anon = _factory.CreateClient();
        var resp = await anon.GetAsync("/api/admin/audit");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
