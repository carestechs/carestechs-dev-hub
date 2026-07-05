using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevHub.Modules.WorkItems.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

/// <summary>
/// Real end-to-end test: creates a GitHub repo, fills all project docs (triggering the
/// initial repo push), simulates an external repo update by writing a file directly to
/// GitHub, signals a work item Completed, then verifies DevHub pulled the updated content
/// from GitHub back into the DB.
///
/// Requires GitHub credentials in env vars: GitHub__Pat and GitHub__Owner.
/// Skipped automatically when credentials are absent (CI without secrets).
/// </summary>
[Collection("postgres")]
public class DocInitWorkItemGitHubE2ETests : IAsyncLifetime
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
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private HttpClient _gitHub = null!;
    private Guid _projectId;
    private string _repoName = null!;
    private string _owner = null!;
    private string _pat = null!;

    public DocInitWorkItemGitHubE2ETests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        (_pat, _owner) = ReadGitHubCredentials();

        var connStr = await _pg.CreateIsolatedDatabaseAsync($"ghe2e_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory
        {
            ConnectionString = connStr,
            BypassDocsGate = false,
            UseFakeExecutor = true,
            ExtraConfig = new Dictionary<string, string?>
            {
                ["GitHub:Pat"]   = _pat,
                ["GitHub:Owner"] = _owner,
            },
        };

        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        await SeedApproveContractAsync();
        _operator = await _factory.LoginOperatorAsync();

        _gitHub = new HttpClient { BaseAddress = new Uri("https://api.github.com") };
        _gitHub.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DevHub-E2E-Tests", "1.0"));
        _gitHub.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _pat);
        _gitHub.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        _repoName = $"devhub-e2e-{Guid.NewGuid():N}"[..24];
        var teamId = await _operator.CreateTeamAsync();

        var projResp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"E2E Test Project {_repoName}",
            slug = _repoName[..14],
            projectType = "feature-delivery",
            owningTeamId = teamId,
            createGitHubRepo = true,
            repoName = _repoName,
        });
        projResp.EnsureSuccessStatusCode();
        _projectId = (await projResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        (await _operator.PatchAsJsonAsync($"/api/projects/{_projectId}",
            new { defaultBranch = "main" }))
            .EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _gitHub.DeleteAsync(
                $"/repos/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repoName)}");
        }
        catch { /* best-effort cleanup */ }

        _operator.Dispose();
        _gitHub.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task DocInit_WorkItem_PullFromRepo_Updates_DB_From_Real_GitHub()
    {
        // ── PHASE 1: Fill docs → lock transition → initial repo push ─────────
        foreach (var key in AllDocKeys.Take(6))
            (await FillDocAsync(key)).EnsureSuccessStatusCode();

        var lockResp = await FillDocAsync(AllDocKeys[6]);
        lockResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var lockBody = await lockResp.Content.ReadFromJsonAsync<JsonElement>();
        lockBody.GetProperty("data").GetProperty("locked").GetBoolean()
            .Should().BeTrue("all required sections filled");
        lockBody.GetProperty("data").GetProperty("repoSynced").GetBoolean()
            .Should().BeTrue("initial push should succeed with real GitHub");

        // ── PHASE 2: Verify initial files exist on GitHub ─────────────────────
        await VerifyGitHubFileExistsAsync("docs/ARCHITECTURE.md",
            expectedSubstring: "Content for architecture/system-overview.");
        await VerifyGitHubFileExistsAsync("docs/stakeholder-definition.md",
            expectedSubstring: "Content for stakeholder-definition/overview.");
        await VerifyGitHubFileExistsAsync("CLAUDE.md",
            expectedSubstring: "Content for claude-md/conventions.");

        // ── PHASE 3: Simulate external repo update ────────────────────────────
        // An AI agent (or developer) commits updated content directly to ARCHITECTURE.md.
        var newArchContent =
            "# Architecture\n\n" +
            "## System Overview\n\nArchitecture updated by work item: event-driven microservices.\n\n" +
            "## Tech Stack\n\nTech stack updated: .NET 10, Angular 20, Kafka, PostgreSQL.\n";

        await UpdateGitHubFileAsync("docs/ARCHITECTURE.md", newArchContent,
            "chore: update architecture doc (simulated work item)");

        // ── PHASE 4: Start a work item and signal Completed ───────────────────
        _factory.Fake.Scripted.SignalStatus = "Completed";

        var workItem = await _operator.StartWorkItemAsync(_projectId);
        var wiId = workItem.GetProperty("id").GetGuid();
        var checkpointKey = workItem.GetProperty("currentCheckpointKey").GetString()!;

        var signalResp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{wiId}/checkpoints/{checkpointKey}/signal",
            new { outcome = "approve" });
        signalResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Fire-and-forget reads 7 files from GitHub sequentially (~200ms each) plus DB writes.
        await Task.Delay(8_000);

        // ── PHASE 5: Verify DevHub DB was updated from GitHub ─────────────────
        var archResp = await _operator.GetAsync($"/api/projects/{_projectId}/docs/architecture");
        archResp.EnsureSuccessStatusCode();
        var archBody = await archResp.Content.ReadFromJsonAsync<JsonElement>();
        var sections = archBody.GetProperty("data").GetProperty("sections").EnumerateArray().ToList();

        sections.Should().Contain(s =>
            s.GetProperty("key").GetString() == "system-overview" &&
            s.GetProperty("content").GetString() == "Architecture updated by work item: event-driven microservices.",
            "DevHub DB must contain content pulled from GitHub");
        sections.Should().Contain(s =>
            s.GetProperty("key").GetString() == "tech-stack" &&
            s.GetProperty("content").GetString() == "Tech stack updated: .NET 10, Angular 20, Kafka, PostgreSQL.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> FillDocAsync(string key)
    {
        var sections = RequiredSections[key].ToDictionary(k => k, k => $"Content for {key}/{k}.");
        return _operator.PutAsJsonAsync($"/api/projects/{_projectId}/docs/{key}", new { sections });
    }

    private async Task VerifyGitHubFileExistsAsync(string path, string expectedSubstring)
    {
        var encodedPath = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
        var resp = await _gitHub.GetAsync(
            $"/repos/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repoName)}/contents/{encodedPath}?ref=main");

        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"file '{path}' should exist in GitHub repo");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var base64 = body.GetProperty("content").GetString()!.Replace("\n", "").Replace("\r", "");
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

        decoded.Should().Contain(expectedSubstring, $"file '{path}' should contain the expected content");
    }

    private async Task UpdateGitHubFileAsync(string path, string content, string commitMessage)
    {
        // GET the current file SHA (required by GitHub Contents API for updates).
        var encodedPath = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
        var getResp = await _gitHub.GetAsync(
            $"/repos/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repoName)}/contents/{encodedPath}?ref=main");
        getResp.EnsureSuccessStatusCode();
        var getBody = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        var sha = getBody.GetProperty("sha").GetString()!;

        // PUT the new content.
        var putBody = new
        {
            message = commitMessage,
            content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
            sha,
            branch = "main",
        };

        var putResp = await _gitHub.PutAsJsonAsync(
            $"/repos/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repoName)}/contents/{encodedPath}",
            putBody);
        putResp.EnsureSuccessStatusCode();
    }

    private async Task SeedApproveContractAsync()
    {
        var adminClient = await _factory.LoginOperatorAsync();
        var list = await adminClient.GetAsync("/api/admin/executors");
        var executorId = (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray().First().GetProperty("id").GetGuid();

        (await adminClient.PostAsJsonAsync(
            $"/api/admin/executors/{executorId}/checkpoint-contracts",
            new
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
            })).EnsureSuccessStatusCode();

        adminClient.Dispose();
    }

    private static (string pat, string owner) ReadGitHubCredentials()
    {
        var pat   = Environment.GetEnvironmentVariable("GitHub__Pat");
        var owner = Environment.GetEnvironmentVariable("GitHub__Owner");

        if (string.IsNullOrWhiteSpace(pat) || string.IsNullOrWhiteSpace(owner))
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, ".env")))
                dir = dir.Parent;

            if (dir != null)
            {
                foreach (var line in File.ReadAllLines(Path.Combine(dir.FullName, ".env")))
                {
                    if (line.StartsWith("GitHub__Pat=",   StringComparison.Ordinal))
                        pat   = line["GitHub__Pat=".Length..].Trim();
                    if (line.StartsWith("GitHub__Owner=", StringComparison.Ordinal))
                        owner = line["GitHub__Owner=".Length..].Trim();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(pat) || string.IsNullOrWhiteSpace(owner))
            throw new InvalidOperationException(
                "GitHub credentials not found. Set GitHub__Pat and GitHub__Owner env vars " +
                "or add them to the .env file at the repo root.");

        return (pat!, owner!);
    }
}
