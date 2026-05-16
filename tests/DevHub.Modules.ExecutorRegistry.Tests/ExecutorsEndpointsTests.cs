using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.Audit;
using DevHub.Modules.ExecutorRegistry.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.ExecutorRegistry.Tests;

[Collection("postgres")]
public class ExecutorsEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;

    public ExecutorsEndpointsTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"er_{Guid.NewGuid():N}");
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

    private static object NewExecutorBody(string? key = null) => new
    {
        key = key ?? $"exec-{Guid.NewGuid():N}".Substring(0, 20),
        displayName = "Test Executor",
        baseUrl = "http://localhost:9000",
        credentialsRef = $"TEST_VAR_{Guid.NewGuid():N}".Substring(0, 20),
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
    };

    [Fact]
    public async Task Create_as_operator_returns_201_and_audits_granted()
    {
        var resp = await _operator.PostAsJsonAsync("/api/admin/executors", NewExecutorBody());
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var entries = await _factory.AuditEntriesForActionAsync("executor:create");
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Granted);
    }

    [Fact]
    public async Task Create_as_non_operator_returns_403_and_audits_denied()
    {
        var (alice, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        var resp = await alice.PostAsJsonAsync("/api/admin/executors", NewExecutorBody());
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var entries = await _factory.AuditEntriesForActionAsync("executor:create");
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Denied);
    }

    [Fact]
    public async Task Create_with_unknown_required_role_key_returns_400()
    {
        var body = new
        {
            key = $"x-{Guid.NewGuid():N}".Substring(0, 12),
            displayName = "X",
            baseUrl = "http://localhost:9000",
            credentialsRef = "TEST_VAR",
            checkpointContracts = new[]
            {
                new
                {
                    checkpointKey = "approve",
                    displayName = "Approve",
                    requiredRoleKey = "nope-not-a-role",
                    allowedOutcomes = new[] { "approve" },
                },
            },
        };
        var resp = await _operator.PostAsJsonAsync("/api/admin/executors", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_with_duplicate_key_returns_409()
    {
        var key = $"dup-{Guid.NewGuid():N}".Substring(0, 16);
        var first = await _operator.PostAsJsonAsync("/api/admin/executors", NewExecutorBody(key));
        first.EnsureSuccessStatusCode();

        var second = await _operator.PostAsJsonAsync("/api/admin/executors", NewExecutorBody(key));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Patch_status_and_displayName_round_trip()
    {
        var created = await _operator.PostAsJsonAsync("/api/admin/executors", NewExecutorBody());
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var patch = await _operator.PatchAsJsonAsync($"/api/admin/executors/{id}", new
        {
            displayName = "Renamed",
            status = "Paused",
        });
        patch.EnsureSuccessStatusCode();
        var patched = (await patch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        patched.GetProperty("displayName").GetString().Should().Be("Renamed");
        patched.GetProperty("status").GetString().Should().Be("Paused");
    }

    [Fact]
    public async Task Replace_contracts_atomically_replaces_set()
    {
        var created = await _operator.PostAsJsonAsync("/api/admin/executors", NewExecutorBody());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var replace = await _operator.PostAsJsonAsync($"/api/admin/executors/{id}/checkpoint-contracts", new
        {
            checkpointContracts = new[]
            {
                new { checkpointKey = "review", displayName = "Review",
                    requiredRoleKey = "operator", allowedOutcomes = new[] { "approve", "revise" } },
                new { checkpointKey = "ship", displayName = "Ship",
                    requiredRoleKey = "operator", allowedOutcomes = new[] { "approve" } },
            },
        });
        replace.EnsureSuccessStatusCode();
        var body = (await replace.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var contracts = body.GetProperty("checkpointContracts");
        contracts.GetArrayLength().Should().Be(2);
        contracts.EnumerateArray().Select(c => c.GetProperty("checkpointKey").GetString())
            .Should().BeEquivalentTo("review", "ship");
    }

    [Fact]
    public async Task Delete_with_active_binding_returns_409()
    {
        var created = await _operator.PostAsJsonAsync("/api/admin/executors", NewExecutorBody());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var bound = await _operator.PostAsJsonAsync("/api/admin/executor-bindings", new
        {
            projectType = $"pt-{Guid.NewGuid():N}".Substring(0, 12),
            executorId = id,
        });
        bound.EnsureSuccessStatusCode();

        var del = await _operator.DeleteAsync($"/api/admin/executors/{id}");
        del.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Delete_without_active_binding_soft_deletes_and_disappears_from_list()
    {
        var created = await _operator.PostAsJsonAsync("/api/admin/executors", NewExecutorBody());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var del = await _operator.DeleteAsync($"/api/admin/executors/{id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _operator.GetAsync($"/api/admin/executors/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await _operator.GetAsync("/api/admin/executors?pageSize=200");
        list.EnsureSuccessStatusCode();
        var ids = (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray().Select(e => e.GetProperty("id").GetGuid());
        ids.Should().NotContain(id);
    }

    [Fact]
    public async Task Response_never_includes_resolved_secret_value()
    {
        // Set a known, recognizable secret in the env, register an executor pointing at it,
        // then assert no response body contains the secret literal.
        var varName = $"TEST_SECRET_{Guid.NewGuid():N}".Substring(0, 24);
        var secretValue = $"super-secret-{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(varName, secretValue);
        try
        {
            var create = await _operator.PostAsJsonAsync("/api/admin/executors", new
            {
                key = $"sec-{Guid.NewGuid():N}".Substring(0, 12),
                displayName = "Secret Carrier",
                baseUrl = "http://localhost:9000",
                credentialsRef = varName,
                checkpointContracts = Array.Empty<object>(),
            });
            create.EnsureSuccessStatusCode();
            var id = (await create.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("data").GetProperty("id").GetGuid();

            // Cycle every endpoint that returns an executor and assert no body leaks the secret.
            var bodies = new List<string>
            {
                await create.Content.ReadAsStringAsync(),
                await (await _operator.GetAsync($"/api/admin/executors/{id}")).Content.ReadAsStringAsync(),
                await (await _operator.GetAsync("/api/admin/executors?pageSize=200")).Content.ReadAsStringAsync(),
            };
            foreach (var b in bodies) b.Should().NotContain(secretValue);
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    // PATCH JSON helper — HttpClient doesn't ship PatchAsJsonAsync as an extension by default
    // on every TFM; declare it locally for clarity.
}

internal static class HttpClientPatchExtensions
{
    public static async Task<HttpResponseMessage> PatchAsJsonAsync<T>(this HttpClient client, string url, T value)
    {
        var req = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(value),
        };
        return await client.SendAsync(req);
    }
}
