using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.Workspace.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

/// <summary>
/// FEAT-015 / T-014 — DocTemplateVersionsController integration tests.
/// </summary>
[Collection("postgres")]
public class DocTemplateVersionTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;

    public DocTemplateVersionTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"dtv_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // GET /api/admin/doc-templates
    // -------------------------------------------------------------------------

    [Fact]
    public async Task List_Returns_Seeded_Version_One_As_Active()
    {
        var resp = await _operator.GetAsync("/api/admin/doc-templates");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();

        items.Should().ContainSingle("only version 1 exists after fresh migration");
        var v = items[0];
        v.GetProperty("versionNumber").GetInt32().Should().Be(1);
        v.GetProperty("isActive").GetBoolean().Should().BeTrue();
        v.GetProperty("sectionCount").GetInt32().Should().Be(22);
        v.GetProperty("projectCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task List_As_Non_Operator_Returns_403()
    {
        var (member, _) = await _factory.LoginFreshMemberAsync(
            $"m-{Guid.NewGuid():N}@test.local", "Pw_M_123!", "NonOp");

        var resp = await member.GetAsync("/api/admin/doc-templates");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        member.Dispose();
    }

    // -------------------------------------------------------------------------
    // POST /api/admin/doc-templates  (create)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_Copies_Sections_From_Source_And_Starts_Inactive()
    {
        var v1Id = await GetVersionOneIdAsync();

        var resp = await _operator.PostAsJsonAsync("/api/admin/doc-templates", new
        {
            sourceVersionId = v1Id,
            notes = "Test copy",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.GetProperty("data");

        dto.GetProperty("versionNumber").GetInt32().Should().Be(2);
        dto.GetProperty("isActive").GetBoolean().Should().BeFalse("new versions start inactive");
        dto.GetProperty("sectionCount").GetInt32().Should().Be(22, "same sections as source version");
        dto.GetProperty("notes").GetString().Should().Be("Test copy");
    }

    [Fact]
    public async Task Create_With_Unknown_Source_Returns_404()
    {
        var resp = await _operator.PostAsJsonAsync("/api/admin/doc-templates", new
        {
            sourceVersionId = Guid.NewGuid(),
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_As_Non_Operator_Returns_403()
    {
        var (member, _) = await _factory.LoginFreshMemberAsync(
            $"m-{Guid.NewGuid():N}@test.local", "Pw_M_123!", "NonOp");
        var v1Id = await GetVersionOneIdAsync();

        var resp = await member.PostAsJsonAsync("/api/admin/doc-templates", new { sourceVersionId = v1Id });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        member.Dispose();
    }

    // -------------------------------------------------------------------------
    // POST /api/admin/doc-templates/{id}/activate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Activate_New_Version_Deactivates_Previous_Active()
    {
        // Create a second version.
        var v1Id = await GetVersionOneIdAsync();
        var createResp = await _operator.PostAsJsonAsync("/api/admin/doc-templates", new { sourceVersionId = v1Id });
        createResp.EnsureSuccessStatusCode();
        var v2Id = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Activate v2.
        var activateResp = await _operator.PostAsJsonAsync($"/api/admin/doc-templates/{v2Id}/activate", new { });
        activateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var activatedDto = (await activateResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data");

        activatedDto.GetProperty("isActive").GetBoolean().Should().BeTrue();
        activatedDto.GetProperty("versionNumber").GetInt32().Should().Be(2);

        // Verify v1 is now inactive.
        var listResp = await _operator.GetAsync("/api/admin/doc-templates");
        var versions = (await listResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray().ToList();

        versions.Should().HaveCount(2);
        versions.Single(v => v.GetProperty("versionNumber").GetInt32() == 1)
            .GetProperty("isActive").GetBoolean().Should().BeFalse("v1 was deactivated");
        versions.Single(v => v.GetProperty("versionNumber").GetInt32() == 2)
            .GetProperty("isActive").GetBoolean().Should().BeTrue("v2 is now active");
    }

    [Fact]
    public async Task Activate_Already_Active_Version_Returns_Ok_Idempotently()
    {
        var v1Id = await GetVersionOneIdAsync();

        var resp = await _operator.PostAsJsonAsync($"/api/admin/doc-templates/{v1Id}/activate", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        dto.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Activate_Unknown_Version_Returns_404()
    {
        var resp = await _operator.PostAsJsonAsync($"/api/admin/doc-templates/{Guid.NewGuid()}/activate", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Activate_As_Non_Operator_Returns_403()
    {
        var (member, _) = await _factory.LoginFreshMemberAsync(
            $"m-{Guid.NewGuid():N}@test.local", "Pw_M_123!", "NonOp");
        var v1Id = await GetVersionOneIdAsync();

        var resp = await member.PostAsJsonAsync($"/api/admin/doc-templates/{v1Id}/activate", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        member.Dispose();
    }

    // -------------------------------------------------------------------------
    // Project creation uses the active template version
    // -------------------------------------------------------------------------

    [Fact]
    public async Task New_Project_Pins_Active_Template_Version()
    {
        var teamResp = await _operator.PostAsJsonAsync("/api/teams", new { name = $"T-{Guid.NewGuid():N}" });
        teamResp.EnsureSuccessStatusCode();
        var teamId = (await teamResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var projResp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"DTVTest-{Guid.NewGuid():N}",
            slug = $"dtvt-{Guid.NewGuid():N}"[..14],
            projectType = "feature-delivery",
            owningTeamId = teamId,
        });
        projResp.EnsureSuccessStatusCode();
        var projId = (await projResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Docs list should be non-empty (pinned to version 1's 22 sections across 7 keys).
        var docsResp = await _operator.GetAsync($"/api/projects/{projId}/docs");
        docsResp.EnsureSuccessStatusCode();
        var docs = (await docsResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray().ToList();

        docs.Should().HaveCount(7);
        docs.Sum(d => d.GetProperty("totalSectionCount").GetInt32()).Should().Be(22);
    }

    // -------------------------------------------------------------------------

    private async Task<Guid> GetVersionOneIdAsync()
    {
        var resp = await _operator.GetAsync("/api/admin/doc-templates");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").EnumerateArray()
            .First(v => v.GetProperty("versionNumber").GetInt32() == 1)
            .GetProperty("id").GetGuid();
    }
}
