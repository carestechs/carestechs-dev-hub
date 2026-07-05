using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.Workspace;
using DevHub.Modules.Workspace.Services;
using DevHub.Modules.WorkItems.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

/// <summary>
/// FEAT-016 — Work-item "Completed" terminal state triggers PullDocsFromRepoAsync:
/// DevHub reads the current doc files from the GitHub repo and syncs them into the DB.
/// </summary>
[Collection("postgres")]
public class WorkItemDocSyncTests : IAsyncLifetime
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
    private StubGitHubService _stub = null!;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public WorkItemDocSyncTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _stub = new StubGitHubService();
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"wids_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory
        {
            ConnectionString = connStr,
            BypassDocsGate = false,
            UseFakeExecutor = true,
            ServiceOverrides =
            [
                services =>
                {
                    var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IGitHubService));
                    if (existing is not null) services.Remove(existing);
                    services.AddSingleton<IGitHubService>(_stub);
                },
            ],
        };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        await SeedApproveContractAsync();
        _operator = await _factory.LoginOperatorAsync();

        var teamId = await _operator.CreateTeamAsync();
        (_projectId, _) = await _operator.CreateProjectAsync(teamId);

        (await _operator.PatchAsJsonAsync($"/api/projects/{_projectId}",
            new { repo = "carestechs/test-repo", defaultBranch = "main" }))
            .EnsureSuccessStatusCode();

        // Fill all docs to lock the project (also stores initial content in the stub).
        foreach (var key in AllDocKeys)
        {
            var sections = RequiredSections[key].ToDictionary(k => k, k => $"Initial {key}/{k}.");
            (await _operator.PutAsJsonAsync($"/api/projects/{_projectId}/docs/{key}", new { sections }))
                .EnsureSuccessStatusCode();
        }
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Completed_Signal_Pulls_Updated_Repo_Content_Into_DB()
    {
        // Simulate an external change to architecture.md in the repo (e.g. AI agent committed).
        _stub.SetContent("docs/ARCHITECTURE.md",
            "# Architecture\n\n## System Overview\n\nUpdated system overview from repo.\n\n## Tech Stack\n\nUpdated tech stack from repo.\n");

        var workItem = await _operator.StartWorkItemAsync(_projectId);
        var wiId = workItem.GetProperty("id").GetGuid();
        var checkpointKey = workItem.GetProperty("currentCheckpointKey").GetString()!;

        _factory.Fake.Scripted.SignalStatus = "Completed";

        var signalResp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{wiId}/checkpoints/{checkpointKey}/signal",
            new { outcome = "approve" });
        signalResp.StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(300);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();

        var sectionId = await db.DocTemplateSections.AsNoTracking()
            .Where(s => s.DocKey == "architecture" && s.SectionKey == "system-overview")
            .Select(s => s.Id).FirstOrDefaultAsync();

        var updated = await db.ProjectDocSections.AsNoTracking()
            .Where(ps => ps.ProjectId == _projectId && ps.SectionId == sectionId)
            .Select(ps => ps.Content).FirstOrDefaultAsync();

        updated.Should().Be("Updated system overview from repo.");
    }

    [Fact]
    public async Task Failed_Signal_Does_Not_Pull_Repo()
    {
        _stub.SetContent("docs/ARCHITECTURE.md",
            "# Architecture\n\n## System Overview\n\nShould not be pulled.\n");

        _factory.Fake.Scripted.SignalStatus = "Failed";

        var workItem = await _operator.StartWorkItemAsync(_projectId);
        var wiId = workItem.GetProperty("id").GetGuid();
        var checkpointKey = workItem.GetProperty("currentCheckpointKey").GetString()!;

        var signalResp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{wiId}/checkpoints/{checkpointKey}/signal",
            new { outcome = "approve" });
        signalResp.StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(200);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();

        var sectionId = await db.DocTemplateSections.AsNoTracking()
            .Where(s => s.DocKey == "architecture" && s.SectionKey == "system-overview")
            .Select(s => s.Id).FirstOrDefaultAsync();

        var content = await db.ProjectDocSections.AsNoTracking()
            .Where(ps => ps.ProjectId == _projectId && ps.SectionId == sectionId)
            .Select(ps => ps.Content).FirstOrDefaultAsync();

        content.Should().NotBe("Should not be pulled.", "failed signal must not trigger a repo pull");
    }

    [Fact]
    public async Task Completed_Signal_No_Repo_Set_Does_Not_Fail()
    {
        // Create a project WITHOUT a repo.
        var teamId = await _operator.CreateTeamAsync();
        var (pid, _) = await _operator.CreateProjectAsync(teamId);

        // Fill docs to allow work item creation (bypass gate via factory default BypassDocsGate=false
        // but this project is separate — we just need it unlocked enough to start a work item).
        // Use a factory with BypassDocsGate=true for this project's work item.
        _factory.Fake.Scripted.SignalStatus = "Completed";

        // Signal on the main project (which has a repo) — but stub returns null for all files.
        _stub.ClearContent();

        var workItem = await _operator.StartWorkItemAsync(_projectId);
        var wiId = workItem.GetProperty("id").GetGuid();
        var checkpointKey = workItem.GetProperty("currentCheckpointKey").GetString()!;

        var signalResp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{wiId}/checkpoints/{checkpointKey}/signal",
            new { outcome = "approve" });

        signalResp.StatusCode.Should().Be(HttpStatusCode.OK, "missing repo files must not fail the signal");
    }

    [Fact]
    public async Task Repo_Read_Failure_Does_Not_Affect_Signal_Response()
    {
        _stub.ThrowOnGet = new Exception("GitHub down");
        _factory.Fake.Scripted.SignalStatus = "Completed";

        var workItem = await _operator.StartWorkItemAsync(_projectId);
        var wiId = workItem.GetProperty("id").GetGuid();
        var checkpointKey = workItem.GetProperty("currentCheckpointKey").GetString()!;

        var signalResp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{wiId}/checkpoints/{checkpointKey}/signal",
            new { outcome = "approve" });

        signalResp.StatusCode.Should().Be(HttpStatusCode.OK, "repo read failure must not fail the signal endpoint");
    }

    [Fact]
    public async Task Completed_Signal_Multiple_Docs_Updated_In_Repo()
    {
        _stub.SetContent("docs/ARCHITECTURE.md",
            "# Architecture\n\n## System Overview\n\nNew arch overview.\n\n## Tech Stack\n\nNew tech stack.\n");
        _stub.SetContent("docs/data-model.md",
            "# Data Model\n\n## Entities & Relationships\n\nNew entities definition.\n");

        _factory.Fake.Scripted.SignalStatus = "Completed";

        var workItem = await _operator.StartWorkItemAsync(_projectId);
        var wiId = workItem.GetProperty("id").GetGuid();
        var checkpointKey = workItem.GetProperty("currentCheckpointKey").GetString()!;

        (await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{wiId}/checkpoints/{checkpointKey}/signal",
            new { outcome = "approve" })).EnsureSuccessStatusCode();

        await Task.Delay(300);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();

        async Task<string?> Get(string docKey, string sectionKey)
        {
            var sid = await db.DocTemplateSections.AsNoTracking()
                .Where(s => s.DocKey == docKey && s.SectionKey == sectionKey)
                .Select(s => s.Id).FirstOrDefaultAsync();
            return await db.ProjectDocSections.AsNoTracking()
                .Where(ps => ps.ProjectId == _projectId && ps.SectionId == sid)
                .Select(ps => ps.Content).FirstOrDefaultAsync();
        }

        (await Get("architecture", "system-overview")).Should().Be("New arch overview.");
        (await Get("architecture", "tech-stack")).Should().Be("New tech stack.");
        (await Get("data-model", "entities")).Should().Be("New entities definition.");
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task SeedApproveContractAsync()
    {
        var adminClient = await _factory.LoginOperatorAsync();
        var list = await adminClient.GetAsync("/api/admin/executors");
        var executorId = (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray().First().GetProperty("id").GetGuid();

        (await adminClient.PostAsJsonAsync($"/api/admin/executors/{executorId}/checkpoint-contracts", new
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
}

/// <summary>
/// In-memory GitHub stub that stores file content written via UpsertFileAsync and
/// returns it via GetFileContentAsync. Tests can override individual files to simulate
/// external repo changes (e.g. an AI agent committing new content).
/// </summary>
internal sealed class StubGitHubService : IGitHubService
{
    private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);
    public Exception? ThrowOnGet { get; set; }

    public void SetContent(string path, string content) => _store[path] = content;
    public void ClearContent() => _store.Clear();

    public Task<string> CreateRepoAsync(string repoName, CancellationToken ct)
        => Task.FromResult($"carestechs/{repoName}");

    public Task SeedScaffoldAsync(string targetRepo, CancellationToken ct)
        => Task.CompletedTask;

    public Task UpsertFileAsync(string repo, string path, string content, string branch,
        string commitMessage, CancellationToken ct)
    {
        _store[path] = content;
        return Task.CompletedTask;
    }

    public Task<string?> GetFileContentAsync(string repo, string path, string branch, CancellationToken ct)
    {
        if (ThrowOnGet is not null) throw ThrowOnGet;
        _store.TryGetValue(path, out var content);
        return Task.FromResult(content);
    }
}
