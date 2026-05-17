using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.Notifications;
using DevHub.Modules.Notifications.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevHub.Modules.Notifications.Tests;

/// <summary>
/// FEAT-009 / T-073 — Notifications-side integration tests for the per-task reconciler:
/// per-task pending uniqueness, loop-back semantics (T-001 dismissed → T-002 raised),
/// and backward compatibility with non-per-task contracts.
/// </summary>
[Collection("postgres")]
public class PerTaskPendingActionTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public PerTaskPendingActionTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"ptp_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();

        await SeedContractsAsync();

        _operator = await _factory.LoginOperatorAsync();
        var teamId = await _operator.CreateTeamAsync();
        _projectId = await _operator.CreateProjectAsync(teamId);
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Seeds the executor with both an `approve` (per-task=false) and an
    /// `assignment-confirmed` (per-task=true) contract. Tests pick which one to drive by
    /// configuring the FakeExecutor's StartCheckpointKey + CurrentTaskId.
    /// </summary>
    private async Task SeedContractsAsync()
    {
        var op = await _factory.LoginOperatorAsync();
        var list = await op.GetAsync("/api/admin/executors");
        var executorId = (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray().First().GetProperty("id").GetGuid();

        var resp = await op.PostAsJsonAsync($"/api/admin/executors/{executorId}/checkpoint-contracts", new
        {
            checkpointContracts = new[]
            {
                new
                {
                    checkpointKey = "approve",
                    displayName = "Approve",
                    requiredRoleKey = "operator",
                    allowedOutcomes = new[] { "approve", "reject" },
                    perTask = false,
                },
                new
                {
                    checkpointKey = "assignment-confirmed",
                    displayName = "Confirm task assignment",
                    requiredRoleKey = "operator",
                    allowedOutcomes = new[] { "confirmed" },
                    perTask = true,
                },
            },
        });
        resp.EnsureSuccessStatusCode();
        op.Dispose();
    }

    private async Task ReconcileAsync(Guid workItemId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var rec = scope.ServiceProvider.GetRequiredService<IPendingActionReconciler>();
        await rec.RecomputeForWorkItemAsync(workItemId);
    }

    private async Task<List<(Guid Id, string? TaskId, DateTimeOffset? DismissedAt)>> LoadRowsAsync(Guid workItemId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await db.PendingActionSignals
            .Where(p => p.WorkItemId == workItemId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new ValueTuple<Guid, string?, DateTimeOffset?>(p.Id, p.TaskId, p.DismissedAt))
            .ToListAsync();
    }

    // ---------- AC-2: per-task contract raises rows keyed by TaskId ----------

    [Fact]
    public async Task Per_task_contract_raises_rows_keyed_by_taskId()
    {
        _factory.Fake.Scripted.StartCheckpointKey = "assignment-confirmed";
        _factory.Fake.Scripted.FetchCheckpointKey = "assignment-confirmed";
        _factory.Fake.Scripted.CurrentTaskId = "T-001";

        var workItemId = await _operator.StartWorkItemAsync(_projectId);
        // StartWorkItem already triggers the post-commit reconciler.

        var rows = await LoadRowsAsync(workItemId);
        rows.Should().HaveCount(1);
        rows[0].TaskId.Should().Be("T-001");
        rows[0].DismissedAt.Should().BeNull();
    }

    // ---------- AC-8: loop-back ----------

    [Fact]
    public async Task Loop_back_T001_dismissed_then_T002_raised_each_distinct()
    {
        _factory.Fake.Scripted.StartCheckpointKey = "assignment-confirmed";
        _factory.Fake.Scripted.FetchCheckpointKey = "assignment-confirmed";
        _factory.Fake.Scripted.CurrentTaskId = "T-001";

        var workItemId = await _operator.StartWorkItemAsync(_projectId);
        var afterT1 = await LoadRowsAsync(workItemId);
        afterT1.Should().HaveCount(1);
        afterT1[0].TaskId.Should().Be("T-001");

        // Advance the executor: still parked on assignment-confirmed but for T-002.
        // We need DevHub to learn the new CurrentTaskId — easiest via a fetch (GET).
        var resp = await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{workItemId}");
        resp.EnsureSuccessStatusCode();
        // The previous GET still sees T-001. Mutate the fake AFTER the cache was refreshed
        // and trigger another GET so the WorkItem.CurrentTaskId column updates.
        _factory.Fake.Scripted.CurrentTaskId = "T-002";
        var resp2 = await _operator.GetAsync($"/api/projects/{_projectId}/work-items/{workItemId}");
        resp2.EnsureSuccessStatusCode();

        // Reconcile explicitly — the GET path doesn't trigger the reconciler the way Start /
        // Signal do; for this test we just call it directly.
        await ReconcileAsync(workItemId);

        var afterT2 = await LoadRowsAsync(workItemId);
        // Two rows total: T-001 dismissed, T-002 active.
        var t001 = afterT2.SingleOrDefault(r => r.TaskId == "T-001");
        var t002 = afterT2.SingleOrDefault(r => r.TaskId == "T-002");
        t001.Should().NotBe(default);
        t001.DismissedAt.Should().NotBeNull("T-001's row falls into 'stale' once CurrentTaskId advances");
        t002.Should().NotBe(default);
        t002.DismissedAt.Should().BeNull("T-002 is the new active task");
    }

    // ---------- backward compatibility: per-task=false keys identically to today ----------

    [Fact]
    public async Task Per_task_false_contract_keeps_legacy_null_taskId_keying()
    {
        // Default fake parks on "approve" (per-task=false in the seed).
        _factory.Fake.Scripted.StartCheckpointKey = "approve";
        _factory.Fake.Scripted.FetchCheckpointKey = "approve";
        _factory.Fake.Scripted.CurrentTaskId = "T-this-should-be-ignored";

        var workItemId = await _operator.StartWorkItemAsync(_projectId);
        var rows = await LoadRowsAsync(workItemId);

        rows.Should().HaveCount(1);
        rows[0].TaskId.Should().BeNull(
            "perTask=false on the active contract means the reconciler does not key by task id");
        rows[0].DismissedAt.Should().BeNull();
    }
}
