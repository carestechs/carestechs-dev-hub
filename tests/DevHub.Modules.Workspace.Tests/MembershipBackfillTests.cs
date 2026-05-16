using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.Workspace.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

/// FEAT-005 backfill: when a member is added to a project (or gains a role), the reconciler
/// recomputes pending rows for them — so they see existing waiting checkpoints they're newly
/// responsible for without waiting for the next transition.
[Collection("postgres")]
public class MembershipBackfillTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;

    public MembershipBackfillTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"mbf_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();
        await SeedApproveContractRequiringOperatorRoleAsync();
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task SeedApproveContractRequiringOperatorRoleAsync()
    {
        // We require the `operator` role on the contract so the operator is in the required set
        // for the work item we start below; and so is any member we later add with that role.
        var list = await _operator.GetAsync("/api/admin/executors");
        var executorId = (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray().First().GetProperty("id").GetGuid();
        (await _operator.PostAsJsonAsync($"/api/admin/executors/{executorId}/checkpoint-contracts", new
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
    }

    [Fact]
    public async Task Adding_member_with_required_role_backfills_pending_rows()
    {
        // 1. Operator creates a project + starts a work item → WaitingOnCheckpoint(approve).
        var teamResp = await _operator.PostAsJsonAsync("/api/teams", new { name = $"T-{Guid.NewGuid():N}" });
        var teamId = (await teamResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();
        var slug = $"p-{Guid.NewGuid():N}".Substring(0, 14);
        var projResp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"P-{Guid.NewGuid():N}",
            slug,
            projectType = "feature-delivery",
            owningTeamId = teamId,
        });
        var projectId = (await projResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var startResp = await _operator.PostAsJsonAsync($"/api/projects/{projectId}/work-items", new
        {
            title = "Backfill demo",
            input = new { },
        });
        startResp.EnsureSuccessStatusCode();

        // 2. Alice is freshly invited (not on the project yet) → her pending list is empty.
        var (alice, aliceId) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        var pendingBefore = await alice.GetAsync("/api/notifications/pending");
        pendingBefore.EnsureSuccessStatusCode();
        var bodyBefore = await pendingBefore.Content.ReadFromJsonAsync<JsonElement>();
        bodyBefore.GetProperty("data").GetArrayLength().Should().Be(0);

        // 3. Operator adds Alice with the `operator` role on the project → backfill should run.
        var addResp = await _operator.PostAsJsonAsync($"/api/projects/{projectId}/memberships", new
        {
            memberId = aliceId,
            roleKeys = new[] { "operator" },
        });
        addResp.EnsureSuccessStatusCode();

        // 4. Alice now sees the existing pending checkpoint.
        var pendingAfter = await alice.GetAsync("/api/notifications/pending");
        pendingAfter.EnsureSuccessStatusCode();
        var bodyAfter = await pendingAfter.Content.ReadFromJsonAsync<JsonElement>();
        bodyAfter.GetProperty("data").GetArrayLength().Should().BeGreaterThan(0);
    }
}
