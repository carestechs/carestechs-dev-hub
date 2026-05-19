using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.Workspace.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

/// <summary>
/// IMP-001 — <c>ProjectDto.BoundExecutorProtocol</c> is projected at read time
/// from the active <c>ExecutorBinding</c> for the project's <c>projectType</c>.
/// Populated on single-project loads via <see cref="DevHub.Contracts.Executors.IExecutorRouter"/>;
/// always <c>null</c> on list responses by design (no N+1).
/// </summary>
[Collection("postgres")]
public class ProjectDtoBoundExecutorProtocolTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;

    public ProjectDtoBoundExecutorProtocolTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"pdbep_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory
        {
            ConnectionString = connStr,
            // We seed our own executor + binding per test so the protocol is explicit.
            SeedFeatureDeliveryBinding = false,
        };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> CreateTeamAsync()
    {
        var resp = await _operator.PostAsJsonAsync("/api/teams", new { name = $"T-{Guid.NewGuid():N}" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task<Guid> RegisterExecutorAsync(string protocol)
    {
        var resp = await _operator.PostAsJsonAsync("/api/admin/executors", new
        {
            key = $"e-{Guid.NewGuid():N}".Substring(0, 12),
            displayName = "Exec",
            baseUrl = "http://localhost:9000",
            credentialsRef = "TEST_VAR",
            checkpointContracts = Array.Empty<object>(),
            protocol,
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task<string> CreateBoundProjectAsync(string projectType, Guid teamId)
    {
        var slug = $"p-{Guid.NewGuid():N}".Substring(0, 14);
        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"P-{Guid.NewGuid():N}",
            slug,
            projectType,
            owningTeamId = teamId,
        });
        resp.EnsureSuccessStatusCode();
        return slug;
    }

    [Fact]
    public async Task Get_by_slug_populates_BoundExecutorProtocol_from_active_binding()
    {
        var teamId = await CreateTeamAsync();
        var execId = await RegisterExecutorAsync(protocol: "orchestrator");
        var projectType = $"pt-{Guid.NewGuid():N}".Substring(0, 12);
        (await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new { projectType, executorId = execId }))
            .EnsureSuccessStatusCode();

        var slug = await CreateBoundProjectAsync(projectType, teamId);

        var resp = await _operator.GetAsync($"/api/projects/by-slug/{slug}");
        resp.EnsureSuccessStatusCode();
        var data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("boundExecutorProtocol").GetString().Should().Be("orchestrator");
    }

    [Fact]
    public async Task Get_by_id_reflects_devhub_protocol_when_binding_is_devhub()
    {
        var teamId = await CreateTeamAsync();
        var execId = await RegisterExecutorAsync(protocol: "devhub");
        var projectType = $"pt-{Guid.NewGuid():N}".Substring(0, 12);
        (await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new { projectType, executorId = execId }))
            .EnsureSuccessStatusCode();

        var slug = await CreateBoundProjectAsync(projectType, teamId);
        var bySlug = (await (await _operator.GetAsync($"/api/projects/by-slug/{slug}"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var projectId = bySlug.GetProperty("id").GetGuid();

        var resp = await _operator.GetAsync($"/api/projects/{projectId}");
        resp.EnsureSuccessStatusCode();
        var data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("boundExecutorProtocol").GetString().Should().Be("devhub");
    }

    [Fact]
    public async Task List_always_returns_null_BoundExecutorProtocol()
    {
        // Seed a project bound to an orchestrator executor so we can confirm the field
        // is null on list responses *even when* a single-load would surface it.
        var teamId = await CreateTeamAsync();
        var execId = await RegisterExecutorAsync(protocol: "orchestrator");
        var projectType = $"pt-{Guid.NewGuid():N}".Substring(0, 12);
        (await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new { projectType, executorId = execId }))
            .EnsureSuccessStatusCode();
        await CreateBoundProjectAsync(projectType, teamId);

        var resp = await _operator.GetAsync("/api/projects");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var rows = body.GetProperty("data").EnumerateArray().ToList();
        rows.Should().NotBeEmpty();
        foreach (var row in rows)
        {
            row.TryGetProperty("boundExecutorProtocol", out var bep).Should().BeTrue(
                "every list row must include the field so clients can distinguish 'null by design' from 'absent'");
            bep.ValueKind.Should().Be(JsonValueKind.Null,
                "list responses always pass null to avoid an N+1 router call");
        }
    }
}
