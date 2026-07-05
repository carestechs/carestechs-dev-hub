using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevHub.Modules.Workspace.Exceptions;
using DevHub.Modules.Workspace.Services;
using DevHub.Modules.Workspace.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

/// <summary>
/// FEAT-013 / T-007 — ProjectService.CreateAsync GitHub integration paths.
/// </summary>
[Collection("postgres")]
public class ProjectCreateGitHubTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private Guid _teamId;

    // Each test controls the fake GitHub service directly via this field.
    private FakeGitHubService _fakeGitHub = new();
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;

    public ProjectCreateGitHubTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"pcgh_{Guid.NewGuid():N}");

        _fakeGitHub = new FakeGitHubService();

        _factory = new DevHubApiFactory
        {
            ConnectionString = connStr,
            ServiceOverrides =
            [
                services =>
                {
                    services.RemoveAll<IGitHubService>();
                    services.AddScoped<IGitHubService>(_ => _fakeGitHub);
                },
            ],
        };

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

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateProject_With_CreateGitHubRepo_True_And_GitHub_Succeeds_Sets_Repo()
    {
        _fakeGitHub.NextResult = "carestechs/my-new-project";

        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"My New Project {Guid.NewGuid():N}",
            slug = UniqueSlug(),
            projectType = "feature-delivery",
            owningTeamId = _teamId,
            createGitHubRepo = true,
            repoName = "my-new-project",
        });

        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.GetProperty("data");

        dto.GetProperty("repo").GetString().Should().Be("carestechs/my-new-project");
        // warnings is always serialized; check it is null or empty on success.
        if (dto.TryGetProperty("warnings", out var warnings) && warnings.ValueKind != JsonValueKind.Null)
            warnings.GetArrayLength().Should().Be(0, "no warnings when GitHub succeeds");
        _fakeGitHub.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateProject_With_CreateGitHubRepo_True_And_GitHub_Fails_Returns_Warning()
    {
        _fakeGitHub.ThrowNext = new GitHubApiException("GitHub API returned 422: Validation Failed");

        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"Project With Bad Repo {Guid.NewGuid():N}",
            slug = UniqueSlug(),
            projectType = "feature-delivery",
            owningTeamId = _teamId,
            createGitHubRepo = true,
            repoName = "bad-repo",
        });

        // Project creation still succeeds (non-fatal GitHub failure).
        resp.IsSuccessStatusCode.Should().BeTrue();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.GetProperty("data");

        dto.TryGetProperty("repo", out var repoEl).Should().BeTrue();
        (repoEl.ValueKind == JsonValueKind.Null || repoEl.GetString() == null)
            .Should().BeTrue("repo stays null when GitHub fails");

        dto.GetProperty("warnings").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Contain("githubRepoCreationFailed");
    }

    [Fact]
    public async Task CreateProject_With_CreateGitHubRepo_False_Does_Not_Call_GitHub()
    {
        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"No GitHub Project {Guid.NewGuid():N}",
            slug = UniqueSlug(),
            projectType = "feature-delivery",
            owningTeamId = _teamId,
            createGitHubRepo = false,
        });

        resp.EnsureSuccessStatusCode();
        _fakeGitHub.CallCount.Should().Be(0, "GitHub should not be called when toggle is off");
    }

    [Fact]
    public async Task CreateProject_With_CreateGitHubRepo_True_Uses_RepoName_When_Provided()
    {
        _fakeGitHub.NextResult = "carestechs/explicit-name";

        await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"Explicit Name Test {Guid.NewGuid():N}",
            slug = UniqueSlug(),
            projectType = "feature-delivery",
            owningTeamId = _teamId,
            createGitHubRepo = true,
            repoName = "explicit-name",
        });

        _fakeGitHub.LastRepoName.Should().Be("explicit-name");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private async Task<Guid> CreateTeamAsync()
    {
        var resp = await _operator.PostAsJsonAsync("/api/teams", new
        {
            name = $"GitHub Test Team {Guid.NewGuid():N}",
            description = (string?)null,
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("data").GetProperty("id").GetString()!);
    }

    private static string UniqueSlug()
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        return $"pcgh-{id}";
    }

    private sealed class FakeGitHubService : IGitHubService
    {
        public string NextResult { get; set; } = "carestechs/default-repo";
        public Exception? ThrowNext { get; set; }
        public int CallCount { get; private set; }
        public string? LastRepoName { get; private set; }

        public Task<string> CreateRepoAsync(string repoName, CancellationToken ct)
        {
            CallCount++;
            LastRepoName = repoName;
            if (ThrowNext is not null)
            {
                var ex = ThrowNext;
                ThrowNext = null;
                throw ex;
            }
            return Task.FromResult(NextResult);
        }

        public Task SeedScaffoldAsync(string targetRepo, CancellationToken ct)
            => Task.CompletedTask;

        public Task UpsertFileAsync(string repo, string path, string content, string branch, string commitMessage, CancellationToken ct)
            => Task.CompletedTask;

        public Task<string?> GetFileContentAsync(string repo, string path, string branch, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }
}
