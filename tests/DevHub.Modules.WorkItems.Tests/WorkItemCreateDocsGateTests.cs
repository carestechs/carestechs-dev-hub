using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.WorkItems.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

/// <summary>
/// FEAT-015 — Work item creation gate: all required doc sections must be filled.
/// Uses the real IProjectDocsQuery (BypassDocsGate = false).
/// </summary>
[Collection("postgres")]
public class WorkItemCreateDocsGateTests : IAsyncLifetime
{
    // Required section keys per doc key — must match version-1 seed in the migration.
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

    private static readonly string[] AllDocKeys =
    [
        "stakeholder-definition", "architecture", "data-model",
        "api-spec", "ui-specification", "primary-user-persona", "claude-md",
    ];

    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public WorkItemCreateDocsGateTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"dg_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory
        {
            ConnectionString = connStr,
            UseFakeExecutor = true,
            BypassDocsGate = false,
        };
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

    // -------------------------------------------------------------------------

    [Fact]
    public async Task Start_With_Zero_Docs_Filled_Returns_409_With_Seven_Missing()
    {
        var resp = await _operator.PostAsJsonAsync($"/api/projects/{_projectId}/work-items",
            new { title = "Test", input = new { } });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("type").GetString().Should().Be("/probs/project-docs-incomplete");
        var missing = body.GetProperty("errors").GetProperty("missingDocs")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        missing.Should().HaveCount(7);
    }

    [Fact]
    public async Task Start_With_Six_Of_Seven_Docs_Filled_Returns_409_With_One_Missing()
    {
        // Fill all but "claude-md".
        foreach (var key in AllDocKeys.Where(k => k != "claude-md"))
            await FillDocAsync(key);

        var resp = await _operator.PostAsJsonAsync($"/api/projects/{_projectId}/work-items",
            new { title = "Test", input = new { } });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("type").GetString().Should().Be("/probs/project-docs-incomplete");
        var missing = body.GetProperty("errors").GetProperty("missingDocs")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        missing.Should().ContainSingle().Which.Should().Be("claude-md");
    }

    [Fact]
    public async Task Start_With_All_Seven_Docs_Filled_Succeeds()
    {
        foreach (var key in AllDocKeys)
            await FillDocAsync(key);

        var dto = await _operator.StartWorkItemAsync(_projectId);

        dto.TryGetProperty("id", out _).Should().BeTrue("work item was created");
    }

    // -------------------------------------------------------------------------

    private async Task FillDocAsync(string key)
    {
        var sections = RequiredSections[key]
            .ToDictionary(k => k, k => $"Content for {key}/{k}.");
        var resp = await _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/{key}",
            new { sections });
        resp.EnsureSuccessStatusCode();
    }
}
