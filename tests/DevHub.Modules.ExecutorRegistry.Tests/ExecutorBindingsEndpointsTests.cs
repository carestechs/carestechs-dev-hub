using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.ExecutorRegistry.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.ExecutorRegistry.Tests;

[Collection("postgres")]
public class ExecutorBindingsEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;

    public ExecutorBindingsEndpointsTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"eb_{Guid.NewGuid():N}");
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

    private async Task<Guid> CreateExecutorAsync()
    {
        var resp = await _operator.PostAsJsonAsync("/api/admin/executors", new
        {
            key = $"e-{Guid.NewGuid():N}".Substring(0, 12),
            displayName = "Exec",
            baseUrl = "http://localhost:9000",
            credentialsRef = "TEST_VAR",
            checkpointContracts = Array.Empty<object>(),
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_as_operator_returns_200()
    {
        var execId = await CreateExecutorAsync();
        var resp = await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new
        {
            projectType = $"pt-{Guid.NewGuid():N}".Substring(0, 12),
            executorId = execId,
        });
        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Create_as_non_operator_returns_403()
    {
        var execId = await CreateExecutorAsync();
        var (alice, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        var resp = await alice.PostAsJsonAsync("/api/admin/executor-bindings", new
        {
            projectType = $"pt-{Guid.NewGuid():N}".Substring(0, 12),
            executorId = execId,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_with_duplicate_active_projectType_returns_409()
    {
        var execId = await CreateExecutorAsync();
        var projectType = $"pt-{Guid.NewGuid():N}".Substring(0, 12);

        var first = await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new { projectType, executorId = execId });
        first.EnsureSuccessStatusCode();

        var second = await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new { projectType, executorId = execId });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_with_nonexistent_executor_returns_404()
    {
        var resp = await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new
        {
            projectType = $"pt-{Guid.NewGuid():N}".Substring(0, 12),
            executorId = Guid.NewGuid(),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_soft_deletes_and_is_idempotent()
    {
        var execId = await CreateExecutorAsync();
        var create = await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new
        {
            projectType = $"pt-{Guid.NewGuid():N}".Substring(0, 12),
            executorId = execId,
        });
        create.EnsureSuccessStatusCode();
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var first = await _operator.DeleteAsync($"/api/admin/executor-bindings/{id}");
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await _operator.DeleteAsync($"/api/admin/executor-bindings/{id}");
        second.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
