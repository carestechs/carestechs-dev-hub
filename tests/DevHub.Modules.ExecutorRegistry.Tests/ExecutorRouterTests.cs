using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.Executors;
using DevHub.Modules.ExecutorRegistry.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevHub.Modules.ExecutorRegistry.Tests;

[Collection("postgres")]
public class ExecutorRouterTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;

    public ExecutorRouterTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"rt_{Guid.NewGuid():N}");
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

    private async Task<TResult> WithRouterAsync<TResult>(Func<IExecutorRouter, Task<TResult>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var router = scope.ServiceProvider.GetRequiredService<IExecutorRouter>();
        return await action(router);
    }

    [Fact]
    public async Task IsProjectTypeBoundAsync_returns_true_for_seeded_feature_delivery()
    {
        // TestRegistrySeeder seeds a feature-delivery binding for every factory by default.
        var bound = await WithRouterAsync(r => r.IsProjectTypeBoundAsync("feature-delivery"));
        bound.Should().BeTrue();
    }

    [Fact]
    public async Task IsProjectTypeBoundAsync_returns_false_for_unknown_type()
    {
        var bound = await WithRouterAsync(r => r.IsProjectTypeBoundAsync("totally-unknown-type"));
        bound.Should().BeFalse();
    }

    [Fact]
    public async Task IsProjectTypeBoundAsync_returns_false_after_binding_delete()
    {
        var execId = await CreateExecutorAsync();
        var pt = $"pt-{Guid.NewGuid():N}".Substring(0, 12);
        var create = await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new
        {
            projectType = pt,
            executorId = execId,
        });
        create.EnsureSuccessStatusCode();
        var bindingId = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        (await WithRouterAsync(r => r.IsProjectTypeBoundAsync(pt))).Should().BeTrue();

        await _operator.DeleteAsync($"/api/admin/executor-bindings/{bindingId}");

        (await WithRouterAsync(r => r.IsProjectTypeBoundAsync(pt))).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_returns_descriptor_for_bound_project()
    {
        // Create a team + project of the seeded feature-delivery type, then resolve.
        var team = await _operator.PostAsJsonAsync("/api/teams", new { name = $"T-{Guid.NewGuid():N}" });
        var teamId = (await team.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var proj = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"P-{Guid.NewGuid():N}",
            slug = $"p-{Guid.NewGuid():N}".Substring(0, 14),
            projectType = "feature-delivery",
            owningTeamId = teamId,
        });
        proj.EnsureSuccessStatusCode();
        var projectId = (await proj.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var descriptor = await WithRouterAsync(r => r.ResolveAsync(projectId));
        descriptor.Should().NotBeNull();
        descriptor!.Key.Should().Be(TestRegistrySeeder.ExecutorKey);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_for_unknown_project()
    {
        var descriptor = await WithRouterAsync(r => r.ResolveAsync(Guid.NewGuid()));
        descriptor.Should().BeNull();
    }

    [Fact]
    public async Task GetCheckpointContractAsync_returns_descriptor_for_known_key()
    {
        var execId = await CreateExecutorWithApproveContractAsync();
        var c = await WithRouterAsync(r => r.GetCheckpointContractAsync(execId, "approve"));
        c.Should().NotBeNull();
        c!.RequiredRoleKey.Should().Be("operator");
        c.AllowedOutcomes.Should().Contain("approve");
    }

    [Fact]
    public async Task GetCheckpointContractAsync_returns_null_for_unknown_key()
    {
        var execId = await CreateExecutorWithApproveContractAsync();
        var c = await WithRouterAsync(r => r.GetCheckpointContractAsync(execId, "definitely-not-there"));
        c.Should().BeNull();
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

    private async Task<Guid> CreateExecutorWithApproveContractAsync()
    {
        var resp = await _operator.PostAsJsonAsync("/api/admin/executors", new
        {
            key = $"e-{Guid.NewGuid():N}".Substring(0, 12),
            displayName = "Exec",
            baseUrl = "http://localhost:9000",
            credentialsRef = "TEST_VAR",
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
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();
    }
}
