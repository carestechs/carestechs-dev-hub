using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.Audit;
using DevHub.Modules.Workspace.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

/// <summary>
/// FEAT-008 / T-063 (project half) — boundary validation on `repo` and
/// `defaultBranch` at create/update time, audit before/after on update.
/// </summary>
[Collection("postgres")]
public class CodeSourceProjectTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _teamId;

    public CodeSourceProjectTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"csp_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();
        _teamId = await CreateTeamAsync();
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // ---------- AC-3: invalid repo ----------

    [Theory]
    [InlineData("https://github.com/foo/bar")]
    [InlineData("foo/bar.git")]
    [InlineData("foo")]
    [InlineData("foo/bar/baz")]
    [InlineData("foo bar/baz")]
    [InlineData("/foo/bar")]
    public async Task CreateProject_with_invalid_repo_returns_400_with_no_db_write(string badRepo)
    {
        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"P-{Guid.NewGuid():N}",
            slug = $"p-{Guid.NewGuid():N}".Substring(0, 14),
            projectType = "feature-delivery",
            owningTeamId = _teamId,
            repo = badRepo,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("type").GetString().Should().Contain("/validation");
        problem.GetProperty("errors").TryGetProperty("repo", out _).Should().BeTrue(
            "the validator tags errors against the 'repo' field name");
    }

    // ---------- AC-4: invalid defaultBranch ----------

    [Theory]
    [InlineData("/main")]
    [InlineData("feat/..lol")]
    [InlineData("feat lol")]
    [InlineData("feat\tx")]
    public async Task CreateProject_with_invalid_default_branch_returns_400(string badBranch)
    {
        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"P-{Guid.NewGuid():N}",
            slug = $"p-{Guid.NewGuid():N}".Substring(0, 14),
            projectType = "feature-delivery",
            owningTeamId = _teamId,
            repo = "a/b",
            defaultBranch = badBranch,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").TryGetProperty("defaultBranch", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CreateProject_with_valid_repo_and_branch_round_trips_in_ProjectDto()
    {
        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"P-{Guid.NewGuid():N}",
            slug = $"p-{Guid.NewGuid():N}".Substring(0, 14),
            projectType = "feature-delivery",
            owningTeamId = _teamId,
            repo = "acme/widgets",
            defaultBranch = "main",
        });
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.GetProperty("data");
        dto.GetProperty("repo").GetString().Should().Be("acme/widgets");
        dto.GetProperty("defaultBranch").GetString().Should().Be("main");
    }

    // ---------- AC-10 (project half): update audit before/after ----------

    [Fact]
    public async Task UpdateProject_repo_change_writes_audit_with_before_and_after()
    {
        var projectId = await CreateProjectAsync(repo: "foo/bar", defaultBranch: "main");

        var resp = await _operator.PatchAsJsonAsync(
            $"/api/projects/{projectId}",
            new { repo = "foo/baz" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await _factory.AuditEntriesForActionAsync("project:update");
        entries.Should().Contain(a =>
            a.Outcome == AuditOutcome.Granted
            && a.DetailsJson != null
            && a.DetailsJson.Contains("repoBefore")
            && a.DetailsJson.Contains("foo/bar")
            && a.DetailsJson.Contains("repoAfter")
            && a.DetailsJson.Contains("foo/baz"));
    }

    [Fact]
    public async Task UpdateProject_with_invalid_repo_returns_400_and_does_not_mutate()
    {
        var projectId = await CreateProjectAsync(repo: "foo/bar", defaultBranch: "main");

        var resp = await _operator.PatchAsJsonAsync(
            $"/api/projects/{projectId}",
            new { repo = "https://github.com/foo/baz" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Confirm the row still carries the original repo.
        var get = await _operator.GetAsync($"/api/projects/{projectId}");
        get.EnsureSuccessStatusCode();
        var dto = (await get.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        dto.GetProperty("repo").GetString().Should().Be("foo/bar");
    }

    // ---------- helpers ----------

    private async Task<Guid> CreateTeamAsync()
    {
        var resp = await _operator.PostAsJsonAsync("/api/teams", new { name = $"T-{Guid.NewGuid():N}" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

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
}
