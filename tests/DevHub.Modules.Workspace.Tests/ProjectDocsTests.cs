using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.Workspace.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

/// <summary>
/// FEAT-014 / T-010 — ProjectDocsController integration tests.
/// </summary>
[Collection("postgres")]
public class ProjectDocsTests : IAsyncLifetime
{
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
            item.GetProperty("key").GetString().Should().NotBeNullOrEmpty();
            item.GetProperty("label").GetString().Should().NotBeNullOrEmpty();
            item.GetProperty("filled").GetBoolean().Should().BeFalse();
        });
    }

    // -------------------------------------------------------------------------
    // PUT /projects/{id}/docs/{key}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Upsert_Creates_Doc_And_Returns_Filled_State()
    {
        var resp = await _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/stakeholder-definition",
            new { content = "# Stakeholder Definition\n\nThis project exists to…" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.GetProperty("data");

        dto.GetProperty("key").GetString().Should().Be("stakeholder-definition");
        dto.GetProperty("filled").GetBoolean().Should().BeTrue();
        dto.GetProperty("content").GetString().Should().StartWith("# Stakeholder Definition");
    }

    [Fact]
    public async Task Upsert_Updates_Existing_Doc()
    {
        await _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/architecture",
            new { content = "v1" });

        var resp = await _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/architecture",
            new { content = "v2 updated" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("content").GetString().Should().Be("v2 updated");
    }

    [Fact]
    public async Task Upsert_With_Unknown_Key_Returns_400()
    {
        var resp = await _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/not-a-real-key",
            new { content = "anything" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upsert_As_Non_Operator_Returns_403()
    {
        var (member, _) = await _factory.LoginFreshMemberAsync(
            $"m-{Guid.NewGuid():N}@test.local", "Pw_M_123!", "NonOp");

        var resp = await member.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/architecture",
            new { content = "not allowed" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        member.Dispose();
    }

    // -------------------------------------------------------------------------
    // GET /projects/{id}/docs/{key}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_Returns_Full_Content_After_Upsert()
    {
        var content = "# Architecture\n\nModular monolith with Postgres.";
        await _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/architecture",
            new { content });

        var resp = await _operator.GetAsync($"/api/projects/{_projectId}/docs/architecture");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.GetProperty("data");

        dto.GetProperty("key").GetString().Should().Be("architecture");
        dto.GetProperty("content").GetString().Should().Be(content);
        dto.GetProperty("filled").GetBoolean().Should().BeTrue();
        dto.GetProperty("hints").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task List_Reflects_Filled_State_After_Upsert()
    {
        await _operator.PutAsJsonAsync(
            $"/api/projects/{_projectId}/docs/claude-md",
            new { content = "## Tech Stack\n\n.NET 10" });

        var resp = await _operator.GetAsync($"/api/projects/{_projectId}/docs");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();

        var claudeMd = items.First(i => i.GetProperty("key").GetString() == "claude-md");
        claudeMd.GetProperty("filled").GetBoolean().Should().BeTrue();

        var others = items.Where(i => i.GetProperty("key").GetString() != "claude-md");
        others.Should().AllSatisfy(i => i.GetProperty("filled").GetBoolean().Should().BeFalse());
    }
}
