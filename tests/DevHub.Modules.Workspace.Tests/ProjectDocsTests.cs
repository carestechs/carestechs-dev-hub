using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.Workspace.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

/// <summary>
/// FEAT-015 — ProjectDocsController integration tests (section-based API).
/// </summary>
[Collection("postgres")]
public class ProjectDocsTests : IAsyncLifetime
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

    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public ProjectDocsTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"docs_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();

        var teamResp = await _operator.PostAsJsonAsync("/api/teams", new { name = $"T-{Guid.NewGuid():N}" });
        teamResp.EnsureSuccessStatusCode();
        var teamBody = await teamResp.Content.ReadFromJsonAsync<JsonElement>();
        var teamId = teamBody.GetProperty("data").GetProperty("id").GetGuid();

        var projResp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"DocTest-{Guid.NewGuid():N}",
            slug = $"dt-{Guid.NewGuid():N}"[..14],
            projectType = "feature-delivery",
            owningTeamId = teamId,
        });
        projResp.EnsureSuccessStatusCode();
        var projBody = await projResp.Content.ReadFromJsonAsync<JsonElement>();
        _projectId = projBody.GetProperty("data").GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // GET /projects/{id}/docs
    // -------------------------------------------------------------------------

    [Fact]
    public async Task List_Returns_Seven_Items_With_Unfilled_State_For_Fresh_Project()
    {
        var resp = await _operator.GetAsync($"/api/projects/{_projectId}/docs");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();

        items.Should().HaveCount(7, "all doc keys must be represented even if no rows exist");
        items.Should().AllSatisfy(item =>
        {
            item.GetProperty("docKey").GetString().Should().NotBeNullOrEmpty();
            item.GetProperty("label").GetString().Should().NotBeNullOrEmpty();
            item.GetProperty("filled").GetBoolean().Should().BeFalse();
            item.GetProperty("filledSectionCount").GetInt32().Should().Be(0);
            item.GetProperty("totalSectionCount").GetInt32().Should().BeGreaterThan(0);
        });
    }

    // -------------------------------------------------------------------------
    // PUT /projects/{id}/docs/{key}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Upsert_Creates_Doc_And_Returns_Filled_State()
    {
        var resp = await FillDocAsync("stakeholder-definition");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.GetProperty("data");

        dto.GetProperty("docKey").GetString().Should().Be("stakeholder-definition");
        dto.GetProperty("filled").GetBoolean().Should().BeTrue();

        var sections = dto.GetProperty("sections").EnumerateArray().ToList();
        sections.Should().NotBeEmpty();
        sections.First(s => s.GetProperty("key").GetString() == "overview")
            .GetProperty("filled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Upsert_Updates_Existing_Doc()
    {
        await FillDocAsync("architecture");

        var resp = await _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/architecture",
            new { sections = new Dictionary<string, string> { ["system-overview"] = "v2 updated", ["tech-stack"] = "updated stack" } });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.GetProperty("data");

        var overviewSection = dto.GetProperty("sections").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == "system-overview");
        overviewSection.GetProperty("content").GetString().Should().Be("v2 updated");
    }

    [Fact]
    public async Task Upsert_With_Unknown_Key_Returns_400()
    {
        var resp = await _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/not-a-real-key",
            new { sections = new Dictionary<string, string> { ["anything"] = "anything" } });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upsert_With_Unknown_Section_Key_Returns_400()
    {
        var resp = await _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/architecture",
            new { sections = new Dictionary<string, string> { ["no-such-section"] = "content" } });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upsert_As_Non_Operator_Returns_403()
    {
        var (member, _) = await _factory.LoginFreshMemberAsync(
            $"m-{Guid.NewGuid():N}@test.local", "Pw_M_123!", "NonOp");

        var resp = await member.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/architecture",
            new { sections = new Dictionary<string, string> { ["system-overview"] = "not allowed" } });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        member.Dispose();
    }

    // -------------------------------------------------------------------------
    // GET /projects/{id}/docs/{key}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_Returns_Sections_With_Content_After_Upsert()
    {
        await FillDocAsync("architecture");

        var resp = await _operator.GetAsync($"/api/projects/{_projectId}/docs/architecture");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.GetProperty("data");

        dto.GetProperty("docKey").GetString().Should().Be("architecture");
        dto.GetProperty("filled").GetBoolean().Should().BeTrue();

        var sections = dto.GetProperty("sections").EnumerateArray().ToList();
        sections.Should().NotBeEmpty();

        var overview = sections.First(s => s.GetProperty("key").GetString() == "system-overview");
        overview.GetProperty("content").GetString().Should().NotBeNullOrEmpty();
        overview.GetProperty("label").GetString().Should().NotBeNullOrEmpty();
        // hint field must be present (even if null)
        overview.TryGetProperty("hint", out _).Should().BeTrue();
    }

    [Fact]
    public async Task List_Reflects_Filled_State_After_Upsert()
    {
        await FillDocAsync("claude-md");

        var resp = await _operator.GetAsync($"/api/projects/{_projectId}/docs");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();

        var claudeMd = items.First(i => i.GetProperty("docKey").GetString() == "claude-md");
        claudeMd.GetProperty("filled").GetBoolean().Should().BeTrue();
        claudeMd.GetProperty("filledSectionCount").GetInt32().Should().BeGreaterThan(0);

        var others = items.Where(i => i.GetProperty("docKey").GetString() != "claude-md");
        others.Should().AllSatisfy(i => i.GetProperty("filled").GetBoolean().Should().BeFalse());
    }

    [Fact]
    public async Task Partial_Section_Fill_Does_Not_Mark_Doc_As_Filled_When_Required_Sections_Missing()
    {
        // Fill only the optional section, skipping required ones.
        await _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/architecture",
            new { sections = new Dictionary<string, string> { ["deployment"] = "optional content" } });

        var resp = await _operator.GetAsync($"/api/projects/{_projectId}/docs/architecture");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("data").GetProperty("filled").GetBoolean().Should()
            .BeFalse("required sections are not filled");
    }

    // -------------------------------------------------------------------------
    // Doc lock
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Upsert_After_All_Docs_Filled_Returns_409_Locked()
    {
        // Fill all required sections across all docs.
        foreach (var key in RequiredSections.Keys)
            await FillDocAsync(key);

        // Any subsequent PUT on any doc must be rejected with 409 locked.
        var resp = await _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/architecture",
            new { sections = new Dictionary<string, string> { ["system-overview"] = "attempted update" } });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("type").GetString().Should().Be("/probs/project-docs-locked");
    }

    [Fact]
    public async Task List_Returns_Locked_True_After_All_Docs_Filled()
    {
        foreach (var key in RequiredSections.Keys)
            await FillDocAsync(key);

        var resp = await _operator.GetAsync($"/api/projects/{_projectId}/docs");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();

        items.Should().AllSatisfy(item =>
            item.GetProperty("locked").GetBoolean().Should().BeTrue());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Task<HttpResponseMessage> FillDocAsync(string docKey)
    {
        var sections = RequiredSections[docKey]
            .ToDictionary(k => k, k => $"Content for {docKey}/{k}.");
        return _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/{docKey}",
            new { sections });
    }
}
