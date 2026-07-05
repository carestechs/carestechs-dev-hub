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
/// FEAT-016 end-to-end story (spy-based, no real GitHub):
///   1. Project is created with a GitHub repo configured.
///   2. Operator fills all required doc sections → docs lock → initial repo push fires.
///   3. An external agent updates a file in the spy store (simulating a real commit).
///   4. A work item is started and signalled "Completed".
///   5. The terminal signal triggers PullDocsFromRepoAsync → DevHub reads files from the
///      spy store and updates the DB with the externally-changed content.
/// </summary>
[Collection("postgres")]
public class DocInitWorkItemE2ETests : IAsyncLifetime
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
    private SpyGitHubServiceE2E _spy = null!;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public DocInitWorkItemE2ETests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _spy = new SpyGitHubServiceE2E();
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"e2e_{Guid.NewGuid():N}");
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
                    services.AddSingleton<IGitHubService>(_spy);
                },
            ],
        };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();

        await SeedApproveContractAsync();

        _operator = await _factory.LoginOperatorAsync();
        var teamId = await _operator.CreateTeamAsync();
        (_projectId, _) = await _operator.CreateProjectAsync(teamId);

        (await _operator.PatchAsJsonAsync($"/api/projects/{_projectId}",
            new { repo = "carestechs/acme-project", defaultBranch = "main" }))
            .EnsureSuccessStatusCode();
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Full_DocInit_Lock_WorkItem_PullFromRepo_Story()
    {
        // ── PHASE 1: Fill all docs ────────────────────────────────────────────
        foreach (var key in AllDocKeys.Take(6))
        {
            var resp = await FillDocAsync(key);
            resp.StatusCode.Should().Be(HttpStatusCode.OK, $"PUT {key} should succeed before lock");
        }

        _spy.UpsertCalls.Should().BeEmpty("repo push must not fire until all docs are filled");

        // Fill the last doc — triggers lock transition and initial repo push.
        var lockResp = await FillDocAsync(AllDocKeys[6]);
        lockResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var lockBody = await lockResp.Content.ReadFromJsonAsync<JsonElement>();
        lockBody.GetProperty("data").GetProperty("locked").GetBoolean()
            .Should().BeTrue("doc should be locked after all required sections are filled");
        lockBody.GetProperty("data").GetProperty("repoSynced").GetBoolean()
            .Should().BeTrue("initial repo push should have succeeded");

        _spy.UpsertCalls.Should().HaveCount(7, "one file per doc key on initial push");
        _spy.UpsertCalls.Select(c => c.Repo).Should().AllBe("carestechs/acme-project");
        _spy.UpsertCalls.Select(c => c.Branch).Should().AllBe("main");
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

        // Spy now has the initial content stored from the lock push.

        // ── PHASE 2: Verify docs are locked ──────────────────────────────────
        var blockedResp = await FillDocAsync("architecture");
        blockedResp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "locked docs must reject further PUT requests");

        var listResp = await _operator.GetAsync($"/api/projects/{_projectId}/docs");
        listResp.EnsureSuccessStatusCode();
        var listBody = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        listBody.GetProperty("data").EnumerateArray()
            .Should().AllSatisfy(doc =>
                doc.GetProperty("locked").GetBoolean().Should().BeTrue("all docs must be locked"));

        // ── PHASE 3: Simulate external repo update ────────────────────────────
        // An AI agent (or developer) commits new content directly to the repo.
        // We override the spy's stored content for ARCHITECTURE.md and data-model.md.
        _spy.SetContent("docs/ARCHITECTURE.md",
            "# Architecture\n\n## System Overview\n\nRevised system overview written by the work item.\n\n" +
            "## Tech Stack\n\nUpdated: .NET 10, Angular 20, PostgreSQL 16.\n");
        _spy.SetContent("docs/data-model.md",
            "# Data Model\n\n## Entities & Relationships\n\nEntities: Project, Team, Member, WorkItem, DocSection.\n");

        _spy.ResetUpsertCalls(); // clear initial-push calls so post-signal assertions are clean

        // ── PHASE 4: Start a work item and signal Completed ───────────────────
        _factory.Fake.Scripted.SignalStatus = "Completed";

        var workItem = await _operator.StartWorkItemAsync(_projectId);
        var wiId = workItem.GetProperty("id").GetGuid();
        var checkpointKey = workItem.GetProperty("currentCheckpointKey").GetString()!;
        checkpointKey.Should().Be("approve");

        var signalResp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{wiId}/checkpoints/{checkpointKey}/signal",
            new { outcome = "approve" });
        signalResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Doc sync runs fire-and-forget after the signal response.
        await Task.Delay(300);

        // ── PHASE 5: Verify DB was updated from repo ──────────────────────────
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();

        async Task<string?> GetContent(string docKey, string sectionKey)
        {
            var sid = await db.DocTemplateSections.AsNoTracking()
                .Where(s => s.DocKey == docKey && s.SectionKey == sectionKey)
                .Select(s => s.Id).FirstOrDefaultAsync();
            return await db.ProjectDocSections.AsNoTracking()
                .Where(ps => ps.ProjectId == _projectId && ps.SectionId == sid)
                .Select(ps => ps.Content).FirstOrDefaultAsync();
        }

        (await GetContent("architecture", "system-overview"))
            .Should().Be("Revised system overview written by the work item.");
        (await GetContent("architecture", "tech-stack"))
            .Should().Be("Updated: .NET 10, Angular 20, PostgreSQL 16.");
        (await GetContent("data-model", "entities"))
            .Should().Be("Entities: Project, Team, Member, WorkItem, DocSection.");

        // Pull-from-repo does NOT push back to the repo — no new upsert calls.
        _spy.UpsertCalls.Should().BeEmpty("pull-from-repo reads the repo; it does not write back");

        // ── PHASE 6: GET reflects the updated content ─────────────────────────
        var archResp = await _operator.GetAsync($"/api/projects/{_projectId}/docs/architecture");
        archResp.EnsureSuccessStatusCode();
        var archBody = await archResp.Content.ReadFromJsonAsync<JsonElement>();
        var sections = archBody.GetProperty("data").GetProperty("sections").EnumerateArray().ToList();

        sections.Should().Contain(s =>
            s.GetProperty("key").GetString() == "system-overview" &&
            s.GetProperty("content").GetString() == "Revised system overview written by the work item.");
        sections.Should().Contain(s =>
            s.GetProperty("key").GetString() == "tech-stack" &&
            s.GetProperty("content").GetString() == "Updated: .NET 10, Angular 20, PostgreSQL 16.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> FillDocAsync(string key)
    {
        var sections = RequiredSections[key].ToDictionary(k => k, k => $"Content for {key}/{k}.");
        return _operator.PutAsJsonAsync($"/api/projects/{_projectId}/docs/{key}", new { sections });
    }

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
/// In-memory GitHub spy that captures upsert calls for assertions and stores file
/// content so GetFileContentAsync can return it. Supports SetContent() to simulate
/// an external agent committing changes directly to the repo.
/// </summary>
internal sealed class SpyGitHubServiceE2E : IGitHubService
{
    private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Repo, string Path, string Branch)> UpsertCalls { get; } = [];

    public void ResetUpsertCalls() => UpsertCalls.Clear();

    public void SetContent(string path, string content) => _store[path] = content;

    public Task<string> CreateRepoAsync(string repoName, CancellationToken ct)
        => Task.FromResult($"carestechs/{repoName}");

    public Task SeedScaffoldAsync(string targetRepo, CancellationToken ct)
        => Task.CompletedTask;

    public Task UpsertFileAsync(string repo, string path, string content, string branch,
        string commitMessage, CancellationToken ct)
    {
        UpsertCalls.Add((repo, path, branch));
        _store[path] = content;
        return Task.CompletedTask;
    }

    public Task<string?> GetFileContentAsync(string repo, string path, string branch, CancellationToken ct)
    {
        _store.TryGetValue(path, out var content);
        return Task.FromResult(content);
    }
}
