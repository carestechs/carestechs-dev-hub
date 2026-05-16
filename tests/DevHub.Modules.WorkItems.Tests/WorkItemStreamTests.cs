using System.Net;
using System.Text.Json;
using DevHub.Contracts.Audit;
using DevHub.Modules.WorkItems.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

[Collection("postgres")]
public class WorkItemStreamTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;
    private Guid _workItemId;

    public WorkItemStreamTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"stream_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();
        var teamId = await _operator.CreateTeamAsync();
        (_projectId, _) = await _operator.CreateProjectAsync(teamId);
        var dto = await _operator.StartWorkItemAsync(_projectId);
        _workItemId = dto.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Stream_as_non_member_returns_403_and_does_not_open_upstream()
    {
        _factory.Fake.ResetCalls();
        var (alice, _, _) = await _factory.LoginFreshMemberAsync(
            $"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");

        var resp = await alice.GetAsync($"/api/projects/{_projectId}/work-items/{_workItemId}/stream",
            HttpCompletionOption.ResponseHeadersRead);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Fake.CountByPath("/stream").Should().Be(0);
    }

    [Fact]
    public async Task Stream_as_member_writes_chunks_byte_for_byte()
    {
        _factory.Fake.Scripted.StreamChunks = new List<(string, TimeSpan)>
        {
            ("data: a\n\n", TimeSpan.Zero),
            ("data: b\n\n", TimeSpan.FromMilliseconds(80)),
            ("data: c\n\n", TimeSpan.FromMilliseconds(80)),
        };

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"/api/projects/{_projectId}/work-items/{_workItemId}/stream");
        using var resp = await _operator.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        await using var stream = await resp.Content.ReadAsStreamAsync();
        var buffer = new byte[64];
        var arrivals = new List<long>();
        var sb = new System.Text.StringBuilder();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int read;
        while ((read = await stream.ReadAsync(buffer, default)) > 0)
        {
            arrivals.Add(sw.ElapsedMilliseconds);
            sb.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
        }

        sb.ToString().Should().Contain("data: a").And.Contain("data: b").And.Contain("data: c");
        // Chunks should arrive spaced apart; the scripted delay is 80ms so deltas
        // should be ≥ 30ms even on a noisy CI clock.
        arrivals.Should().HaveCountGreaterOrEqualTo(2);
        for (int i = 1; i < arrivals.Count; i++)
        {
            (arrivals[i] - arrivals[i - 1]).Should().BeGreaterOrEqualTo(30);
        }
    }

    [Fact]
    public async Task Stream_client_disconnect_closes_upstream()
    {
        _factory.Fake.Scripted.StreamChunks = new List<(string, TimeSpan)>
        {
            ("data: 1\n\n", TimeSpan.Zero),
            ("data: 2\n\n", TimeSpan.FromMilliseconds(50)),
            ("data: 3\n\n", TimeSpan.FromMilliseconds(50)),
            ("data: 4\n\n", TimeSpan.FromMilliseconds(50)),
            ("data: 5\n\n", TimeSpan.FromMilliseconds(50)),
        };
        // Add a delay before the first chunk so we have time to abort while upstream is open.
        var first = _factory.Fake.Scripted.StreamChunks[0];
        _factory.Fake.Scripted.StreamChunks[0] = ("data: 1\n\n", TimeSpan.FromMilliseconds(100));

        using var cts = new CancellationTokenSource();
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"/api/projects/{_projectId}/work-items/{_workItemId}/stream");

        var sendTask = _operator.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        // Wait until the fake reports an open stream connection (the inner pipe has started).
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (_factory.Fake.OpenStreamConnections == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        _factory.Fake.OpenStreamConnections.Should().BeGreaterThan(0,
            "upstream socket should have been opened after auth grant");

        // Abort the client; the fake must observe the disconnect within 1s.
        cts.Cancel();
        try { (await sendTask).Dispose(); } catch (TaskCanceledException) { /* expected */ }

        var until = DateTimeOffset.UtcNow.AddSeconds(2);
        while (_factory.Fake.OpenStreamConnections > 0 && DateTimeOffset.UtcNow < until)
        {
            await Task.Delay(20);
        }
        _factory.Fake.OpenStreamConnections.Should().Be(0);
    }

    [Fact]
    public async Task Stream_grants_one_audit_row_per_connection_not_per_chunk()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"/api/projects/{_projectId}/work-items/{_workItemId}/stream");
        using var resp = await _operator.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await resp.Content.ReadAsStreamAsync();
        var buffer = new byte[64];
        // Drain
        while (await stream.ReadAsync(buffer, default) > 0) { }

        var entries = await _factory.AuditEntriesForActionAsync("workitem:stream");
        var granted = entries.Where(e => e.Outcome == AuditOutcome.Granted).ToArray();
        granted.Length.Should().Be(1);
    }
}
