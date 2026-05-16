using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.Workspace.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

/// <summary>
/// FEAT-003 AC-2: creating a project whose <c>projectType</c> has no active binding returns 409.
/// Uses <c>SeedFeatureDeliveryBinding = false</c> so the harness does NOT auto-seed a binding.
/// </summary>
[Collection("postgres")]
public class ProjectBindingValidationTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;

    public ProjectBindingValidationTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"pbv_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory
        {
            ConnectionString = connStr,
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

    [Fact]
    public async Task Create_project_with_unbound_projectType_returns_409()
    {
        var teamId = await CreateTeamAsync();
        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"P-{Guid.NewGuid():N}",
            slug = $"p-{Guid.NewGuid():N}".Substring(0, 14),
            projectType = "no-binding-for-me",
            owningTeamId = teamId,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString()!.ToLowerInvariant().Should().Contain("no executor bound");
    }

    [Fact]
    public async Task Create_project_after_binding_added_returns_200()
    {
        var teamId = await CreateTeamAsync();

        // 1. Register an executor + binding for our project type.
        var execResp = await _operator.PostAsJsonAsync("/api/admin/executors", new
        {
            key = $"e-{Guid.NewGuid():N}".Substring(0, 12),
            displayName = "Exec",
            baseUrl = "http://localhost:9000",
            credentialsRef = "TEST_VAR",
            checkpointContracts = Array.Empty<object>(),
        });
        execResp.EnsureSuccessStatusCode();
        var execId = (await execResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var pt = $"pt-{Guid.NewGuid():N}".Substring(0, 12);
        var bind = await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new
        {
            projectType = pt,
            executorId = execId,
        });
        bind.EnsureSuccessStatusCode();

        // 2. Now project creation should pass.
        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"P-{Guid.NewGuid():N}",
            slug = $"p-{Guid.NewGuid():N}".Substring(0, 14),
            projectType = pt,
            owningTeamId = teamId,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_project_after_binding_deleted_returns_409()
    {
        var teamId = await CreateTeamAsync();

        var execResp = await _operator.PostAsJsonAsync("/api/admin/executors", new
        {
            key = $"e-{Guid.NewGuid():N}".Substring(0, 12),
            displayName = "Exec",
            baseUrl = "http://localhost:9000",
            credentialsRef = "TEST_VAR",
            checkpointContracts = Array.Empty<object>(),
        });
        var execId = (await execResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var pt = $"pt-{Guid.NewGuid():N}".Substring(0, 12);
        var bind = await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new
        {
            projectType = pt,
            executorId = execId,
        });
        var bindingId = (await bind.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        await _operator.DeleteAsync($"/api/admin/executor-bindings/{bindingId}");

        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"P-{Guid.NewGuid():N}",
            slug = $"p-{Guid.NewGuid():N}".Substring(0, 14),
            projectType = pt,
            owningTeamId = teamId,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
