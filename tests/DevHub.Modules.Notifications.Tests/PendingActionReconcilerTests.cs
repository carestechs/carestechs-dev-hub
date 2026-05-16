using System.Net.Http.Json;
using DevHub.Contracts.Notifications;
using DevHub.Modules.Notifications.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevHub.Modules.Notifications.Tests;

[Collection("postgres")]
public class PendingActionReconcilerTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _operatorMemberId;
    private Guid _projectId;

    public PendingActionReconcilerTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"rec_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();
        await _operator.SeedApproveContractAsync(requiredRoleKey: "operator");

        // Resolve the seeded operator's member id.
        await using var scope = _factory.Services.CreateAsyncScope();
        var ws = scope.ServiceProvider.GetRequiredService<DevHub.Modules.Workspace.WorkspaceDbContext>();
        _operatorMemberId = await ws.Members.Where(m => m.Email == _factory.OperatorEmail)
            .Select(m => m.Id).FirstAsync();

        var teamId = await _operator.CreateTeamAsync();
        _projectId = await _operator.CreateProjectAsync(teamId);
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> StartWorkItemAsync() => await _operator.StartWorkItemAsync(_projectId);

    private async Task<int> CountActivePendingForWorkItemAsync(Guid workItemId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await db.PendingActionSignals.CountAsync(p => p.WorkItemId == workItemId && p.DismissedAt == null);
    }

    [Fact]
    public async Task WaitingOnCheckpoint_upserts_row_for_operator_member()
    {
        // StartWorkItem triggers the post-commit reconciler from T-043.
        var workItemId = await StartWorkItemAsync();

        var pending = await _operator.ListPendingAsync();
        pending.Should().HaveCount(1);
        pending[0].GetProperty("workItemId").GetGuid().Should().Be(workItemId);
    }

    [Fact]
    public async Task Idempotent_second_call_writes_no_new_rows()
    {
        var workItemId = await StartWorkItemAsync();
        var firstCount = await CountActivePendingForWorkItemAsync(workItemId);

        // Re-run the reconciler explicitly.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var rec = scope.ServiceProvider.GetRequiredService<IPendingActionReconciler>();
            await rec.RecomputeForWorkItemAsync(workItemId);
        }

        var secondCount = await CountActivePendingForWorkItemAsync(workItemId);
        secondCount.Should().Be(firstCount);
    }

    [Fact]
    public async Task Signal_to_completion_dismisses_all_pending_rows()
    {
        var workItemId = await StartWorkItemAsync();
        (await CountActivePendingForWorkItemAsync(workItemId)).Should().BeGreaterThan(0);

        // The fake scripted Signal sets currentStatus to "Completed".
        var resp = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{workItemId}/checkpoints/approve/signal",
            new { outcome = "approve" });
        resp.EnsureSuccessStatusCode();

        (await CountActivePendingForWorkItemAsync(workItemId)).Should().Be(0);
    }

    [Fact]
    public async Task Operator_gets_row_without_being_a_project_member()
    {
        // The seeded operator is workspace-scoped via WorkspaceRoleAssignment, NOT via
        // ProjectMembership. The reconciler should still raise a row.
        var workItemId = await StartWorkItemAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var row = await db.PendingActionSignals
            .FirstOrDefaultAsync(p => p.MemberId == _operatorMemberId && p.WorkItemId == workItemId && p.DismissedAt == null);
        row.Should().NotBeNull();
    }
}
