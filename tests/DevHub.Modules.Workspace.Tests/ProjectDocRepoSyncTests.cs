using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.Workspace.Services;
using DevHub.Modules.Workspace.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

/// <summary>
/// FEAT-016 — Integration tests for the lock-transition repo push path.
/// Uses a SpyGitHubService so no real GitHub API calls are made.
/// </summary>
[Collection("postgres")]
public class ProjectDocRepoSyncTests : IAsyncLifetime
{
    private static readonly Dictionary<string, string[]> RequiredSections = new(StringComparer.Ordinal)
    {
        ["stakeholder-definition"] = ["overview", "personas"],
        ["architecture"]           = ["system-overview", "tech-stack"],
        ["data-model"]             = ["entities"],
        ["api-spec"]               = ["endpoints", "auth"],
        ["ui-specification"]       = ["screens", "interactions"],
        ["primary-user-persona"]   = ["profile", "goals"],
        ["claude-md"]              = ["conventions", "patterns"],
    };

    private static readonly string[] AllDocKeys = [.. RequiredSections.Keys];

    private readonly PostgresFixture _pg;
    private SpyGitHubService _spy = null!;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;
    private Guid _repoProjectId;

    public ProjectDocRepoSyncTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _spy = new SpyGitHubService();
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"sync_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory
        {
            ConnectionString = connStr,
            BypassDocsGate = false,
            ServiceOverrides =
            [
                services =>
                {
                    var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IGitHubService));
                    if (existing is not null) services.Remove(existing);
                    services.AddSingleton<IGitHubService>(_spy);
                },
            ],
        };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();

        var teamResp = await _operator.PostAsJsonAsync("/api/teams", new { name = $"T-{Guid.NewGuid():N}" });
        teamResp.EnsureSuccessStatusCode();
        var teamId = (await teamResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Project without a repo.
        var p1Resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = "No-Repo Project",
            slug = $"nr-{Guid.NewGuid():N}"[..14],
            projectType = "feature-delivery",
            owningTeamId = teamId,
        });
        p1Resp.EnsureSuccessStatusCode();
        _projectId = (await p1Resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Project with a repo set (UPDATE via PATCH).
        var p2Resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = "Repo Project",
            slug = $"rp-{Guid.NewGuid():N}"[..14],
            projectType = "feature-delivery",
            owningTeamId = teamId,
        });
        p2Resp.EnsureSuccessStatusCode();
        _repoProjectId = (await p2Resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Set repo and defaultBranch on the second project.
        var patchResp = await _operator.PatchAsJsonAsync($"/api/projects/{_repoProjectId}",
            new { repo = "carestechs/test-repo", defaultBranch = "main" });
        patchResp.EnsureSuccessStatusCode();
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------

    [Fact]
    public async Task Lock_Transition_On_Project_With_Repo_Pushes_Seven_Files()
    {
        _spy.Reset();

        foreach (var key in AllDocKeys)
            (await FillDocAsync(_repoProjectId, key)).EnsureSuccessStatusCode();

        _spy.UpsertCalls.Should().HaveCount(7, "one file per doc key");
        _spy.UpsertCalls.Select(c => c.Path).Should().BeEquivalentTo(
        [
            "docs/stakeholder-definition.md",
            "docs/ARCHITECTURE.md",
            "docs/data-model.md",
            "docs/api-spec.md",
            "docs/ui-specification.md",
            "docs/personas/primary-user.md",
            "CLAUDE.md",
        ]);
    }

    [Fact]
    public async Task Lock_Transition_Response_Contains_RepoSynced_True_When_Repo_Set()
    {
        _spy.Reset();
        foreach (var key in AllDocKeys.Take(6))
            (await FillDocAsync(_repoProjectId, key)).EnsureSuccessStatusCode();

        // Last doc — triggers lock transition.
        var resp = await FillDocAsync(_repoProjectId, AllDocKeys[6]);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("repoSynced").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Lock_Transition_On_Project_Without_Repo_RepoSynced_False_No_GitHub_Call()
    {
        _spy.Reset();
        foreach (var key in AllDocKeys.Take(6))
            (await FillDocAsync(_projectId, key)).EnsureSuccessStatusCode();

        var resp = await FillDocAsync(_projectId, AllDocKeys[6]);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        // repoSynced is null (no push attempted) — present but null, or absent.
        if (data.TryGetProperty("repoSynced", out var rsEl))
            rsEl.ValueKind.Should().Be(JsonValueKind.Null, "repoSynced should be null when no push was attempted");

        _spy.UpsertCalls.Should().BeEmpty("no repo — no GitHub call");
    }

    [Fact]
    public async Task GitHub_Push_Failure_Does_Not_Fail_The_PUT()
    {
        _spy.Reset();
        _spy.ThrowOnUpsert = new Exception("GitHub API timeout");

        foreach (var key in AllDocKeys.Take(6))
            (await FillDocAsync(_repoProjectId, key)).EnsureSuccessStatusCode();

        var resp = await FillDocAsync(_repoProjectId, AllDocKeys[6]);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "PUT succeeds even when GitHub push fails");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("repoSynced").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Subsequent_PUT_After_Lock_Does_Not_Trigger_Repo_Push()
    {
        // Fill all docs on a fresh project for this test.
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"sync2_{Guid.NewGuid():N}");
        await using var factory2 = new DevHubApiFactory
        {
            ConnectionString = connStr,
            BypassDocsGate = false,
            ServiceOverrides = [services =>
            {
                var ex = services.FirstOrDefault(d => d.ServiceType == typeof(IGitHubService));
                if (ex is not null) services.Remove(ex);
                services.AddSingleton<IGitHubService>(_spy);
            }],
        };
        (await factory2.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        var op2 = await factory2.LoginOperatorAsync();

        var teamResp = await op2.PostAsJsonAsync("/api/teams", new { name = $"T-{Guid.NewGuid():N}" });
        var teamId = (await teamResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();
        var projResp = await op2.PostAsJsonAsync("/api/projects", new
        {
            name = "Repo Project 2", slug = $"rp2-{Guid.NewGuid():N}"[..14],
            projectType = "feature-delivery", owningTeamId = teamId,
        });
        var pid = (await projResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();
        await op2.PatchAsJsonAsync($"/api/projects/{pid}", new { repo = "carestechs/test-repo", defaultBranch = "main" });

        _spy.Reset();
        foreach (var key in AllDocKeys)
            (await op2.PutAsJsonAsync($"/api/projects/{pid}/docs/{key}",
                new { sections = RequiredSections[key].ToDictionary(k => k, k => $"content {k}") }))
            .EnsureSuccessStatusCode();

        var callsAfterLock = _spy.UpsertCalls.Count;
        callsAfterLock.Should().Be(7);

        // Attempt another PUT — should get 409 locked, no further GitHub calls.
        _spy.Reset();
        var locked = await op2.PutAsJsonAsync($"/api/projects/{pid}/docs/architecture",
            new { sections = new Dictionary<string, string> { ["system-overview"] = "updated" } });
        locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _spy.UpsertCalls.Should().BeEmpty("docs are locked; no push should fire");

        op2.Dispose();
    }

    // -------------------------------------------------------------------------
    // T-011: defaultBranch null → silent skip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Lock_Transition_With_Null_DefaultBranch_Skips_Push()
    {
        _spy.Reset();

        var teamResp = await _operator.PostAsJsonAsync("/api/teams", new { name = $"T-{Guid.NewGuid():N}" });
        var teamId = (await teamResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();
        var projResp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = "NoBranch", slug = $"nb-{Guid.NewGuid():N}"[..14],
            projectType = "feature-delivery", owningTeamId = teamId,
        });
        projResp.EnsureSuccessStatusCode();
        var pid = (await projResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Set repo but NO defaultBranch.
        (await _operator.PatchAsJsonAsync($"/api/projects/{pid}", new { repo = "carestechs/test-repo" }))
            .EnsureSuccessStatusCode();

        foreach (var key in AllDocKeys)
            (await FillDocAsync(pid, key)).EnsureSuccessStatusCode();

        _spy.UpsertCalls.Should().BeEmpty("no defaultBranch — push must be skipped silently");
    }

    // -------------------------------------------------------------------------

    private Task<HttpResponseMessage> FillDocAsync(Guid projectId, string key)
    {
        var sections = RequiredSections[key].ToDictionary(k => k, k => $"Content for {key}/{k}.");
        return _operator.PutAsJsonAsync($"/api/projects/{projectId}/docs/{key}", new { sections });
    }
}

// -------------------------------------------------------------------------
// Spy implementation
// -------------------------------------------------------------------------

internal sealed class SpyGitHubService : IGitHubService
{
    public List<(string Repo, string Path, string Branch)> UpsertCalls { get; } = [];
    public Exception? ThrowOnUpsert { get; set; }

    public void Reset()
    {
        UpsertCalls.Clear();
        ThrowOnUpsert = null;
    }

    public Task<string> CreateRepoAsync(string repoName, CancellationToken ct)
        => Task.FromResult($"carestechs/{repoName}");

    public Task SeedScaffoldAsync(string targetRepo, CancellationToken ct)
        => Task.CompletedTask;

    public Task UpsertFileAsync(string repo, string path, string content, string branch, string commitMessage, CancellationToken ct)
    {
        if (ThrowOnUpsert is not null)
            throw ThrowOnUpsert;
        UpsertCalls.Add((repo, path, branch));
        return Task.CompletedTask;
    }

    public Task<string?> GetFileContentAsync(string repo, string path, string branch, CancellationToken ct)
        => Task.FromResult<string?>(null);
}
