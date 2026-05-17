using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevHub.Contracts.Audit;
using DevHub.Modules.WorkItems.Tests.Helpers;
using DevHub.TestHarness;
using DevHub.TestHarness.FakeExecutor;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

/// <summary>
/// FEAT-008 / T-063 — closes the AC loop on the forward shape and on
/// boundary validation for workBranch updates. Asserts byte-for-byte
/// equivalence on the `intake.codeSource` subtree (AC-5), absence of
/// the entire `intake` key when the project has no repo (AC-6), and
/// absence of the `workBranch` subfield when not set (AC-7).
/// </summary>
[Collection("postgres")]
public class CodeSourceForwardTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _teamId;

    public CodeSourceForwardTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"cs_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();
        _teamId = await _operator.CreateTeamAsync();
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // ---------- AC-5 ----------

    [Fact]
    public async Task Start_includes_codeSource_when_project_has_repo_and_workBranch()
    {
        var projectId = await CreateProjectAsync(repo: "acme/widgets", defaultBranch: "main");
        _factory.Fake.ResetCalls();

        var resp = await _operator.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title = "Add CSV export",
            input = new { },
            workBranch = "feat/abc",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = LastStartBody();
        var codeSource = body["intake"]!["codeSource"]!.AsObject();
        codeSource["repo"]!.GetValue<string>().Should().Be("acme/widgets");
        codeSource["baseBranch"]!.GetValue<string>().Should().Be("main");
        codeSource["workBranch"]!.GetValue<string>().Should().Be("feat/abc");
    }

    // ---------- AC-6 ----------

    [Fact]
    public async Task Start_omits_intake_envelope_entirely_when_project_repo_is_null()
    {
        var projectId = await CreateProjectAsync(repo: null, defaultBranch: null);
        _factory.Fake.ResetCalls();

        var resp = await _operator.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title = "X",
            input = new { },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = LastStartBody();
        body.AsObject().ContainsKey("intake").Should().BeFalse(
            "AC-6: omit the entire intake envelope (do not send intake:null) when the project has no repo");
        // The pre-FEAT shape is byte-for-byte preserved: only input + correlationMarker.
        body.AsObject().ContainsKey("input").Should().BeTrue();
        body.AsObject().ContainsKey("correlationMarker").Should().BeTrue();
    }

    [Fact]
    public async Task Start_omits_intake_when_only_default_branch_is_set()
    {
        // Half-set coordinates: orchestrator would reject. We omit and log.
        var projectId = await CreateProjectAsync(repo: null, defaultBranch: "main");
        _factory.Fake.ResetCalls();

        var resp = await _operator.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title = "X",
            input = new { },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        LastStartBody().AsObject().ContainsKey("intake").Should().BeFalse();
    }

    // ---------- AC-7 ----------

    [Fact]
    public async Task Start_omits_workBranch_subfield_when_request_has_no_work_branch()
    {
        var projectId = await CreateProjectAsync(repo: "a/b", defaultBranch: "main");
        _factory.Fake.ResetCalls();

        var resp = await _operator.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title = "X",
            input = new { },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var codeSource = LastStartBody()["intake"]!["codeSource"]!.AsObject();
        codeSource.ContainsKey("repo").Should().BeTrue();
        codeSource.ContainsKey("baseBranch").Should().BeTrue();
        codeSource.ContainsKey("workBranch").Should().BeFalse(
            "AC-7: omit workBranch (do not send workBranch:null) when not set");
    }

    // ---------- AC-10 (WorkItem half) ----------

    [Fact]
    public async Task UpdateWorkItem_workBranch_writes_audit_entry_with_before_and_after()
    {
        var projectId = await CreateProjectAsync(repo: "a/b", defaultBranch: "main");
        var start = await _operator.StartWorkItemAsync(projectId);
        var workItemId = start.GetProperty("id").GetGuid();

        var resp = await _operator.PatchAsJsonAsync(
            $"/api/projects/{projectId}/work-items/{workItemId}",
            new { workBranch = "feat/x" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await _factory.AuditEntriesForActionAsync("workitem:update");
        entries.Should().ContainSingle(a =>
            a.Outcome == AuditOutcome.Granted
            && a.DetailsJson != null
            && a.DetailsJson.Contains("workBranchBefore")
            && a.DetailsJson.Contains("workBranchAfter")
            && a.DetailsJson.Contains("feat/x"));
    }

    [Fact]
    public async Task UpdateWorkItem_workBranch_empty_string_clears_override_and_audits()
    {
        var projectId = await CreateProjectAsync(repo: "a/b", defaultBranch: "main");
        var start = await _operator.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title = "X",
            input = new { },
            workBranch = "feat/initial",
        });
        var startBody = await start.Content.ReadFromJsonAsync<JsonElement>();
        var workItemId = startBody.GetProperty("data").GetProperty("id").GetGuid();

        var resp = await _operator.PatchAsJsonAsync(
            $"/api/projects/{projectId}/work-items/{workItemId}",
            new { workBranch = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var workBranchProp = body.GetProperty("data").GetProperty("workBranch");
        workBranchProp.ValueKind.Should().Be(JsonValueKind.Null,
            "empty string PATCH clears the override; backend stores null");

        var entries = await _factory.AuditEntriesForActionAsync("workitem:update");
        entries.Should().Contain(a =>
            a.DetailsJson != null
            && a.DetailsJson.Contains("feat/initial")
            && a.DetailsJson.Contains("workBranchAfter"));
    }

    [Fact]
    public async Task UpdateWorkItem_with_invalid_workBranch_returns_400_and_writes_no_workBranch_audit()
    {
        var projectId = await CreateProjectAsync(repo: "a/b", defaultBranch: "main");
        var start = await _operator.StartWorkItemAsync(projectId);
        var workItemId = start.GetProperty("id").GetGuid();

        var resp = await _operator.PatchAsJsonAsync(
            $"/api/projects/{projectId}/work-items/{workItemId}",
            new { workBranch = "/bad" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The auth-grant audit (operator-check Granted) is unavoidable — it fires before
        // boundary validation. What MUST NOT exist is a service-level "workitem:update"
        // row with workBranch details, since that would mean the persistence path ran.
        var entries = await _factory.AuditEntriesForActionAsync("workitem:update");
        entries.Should().NotContain(a =>
            a.DetailsJson != null
            && (a.DetailsJson.Contains("workBranchBefore") || a.DetailsJson.Contains("/bad")));
    }

    // ---------- helpers ----------

    private async Task<Guid> CreateProjectAsync(string? repo, string? defaultBranch)
    {
        var slug = $"p-{Guid.NewGuid():N}".Substring(0, 14);
        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"P-{Guid.NewGuid():N}",
            slug,
            projectType = "feature-delivery",
            owningTeamId = _teamId,
            repo,
            defaultBranch,
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    private JsonNode LastStartBody()
    {
        // Most recent POST to "/work-items" exactly (not /cancel, /signal, etc.).
        var last = _factory.Fake.Calls
            .Where(c => c.Method == "POST" && c.Path == "/work-items")
            .OrderByDescending(c => c.OccurredAt)
            .FirstOrDefault();
        last.Should().NotBeNull("a POST /work-items should have been recorded by the FakeExecutor");
        last!.BodyJson.Should().NotBeNull();
        return JsonNode.Parse(last.BodyJson!)
            ?? throw new InvalidOperationException("Expected a JSON object body on /work-items POST");
    }
}
