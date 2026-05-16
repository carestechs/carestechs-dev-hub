using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Modules.Notifications.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Notifications.Tests;

[Collection("postgres")]
public class NotificationStreamTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;
    private Guid _projectId;

    public NotificationStreamTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"nst_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory { ConnectionString = connStr, UseFakeExecutor = true };
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();
        await _operator.SeedApproveContractAsync(requiredRoleKey: "operator");
        var teamId = await _operator.CreateTeamAsync();
        _projectId = await _operator.CreateProjectAsync(teamId);
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// Opens the SSE stream for the operator and returns the stream + reader. Caller must
    /// dispose all resources.
    private async Task<(HttpResponseMessage resp, StreamReader reader)> OpenStreamAsync(CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/stream");
        var resp = await _operator.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        return (resp, new StreamReader(stream));
    }

    /// Reads SSE lines until the first `data: {...}` payload arrives or the cancellation token fires.
    private static async Task<JsonElement?> AwaitFirstEventAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) return null;
                if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    var json = line.Substring("data: ".Length);
                    return JsonSerializer.Deserialize<JsonElement>(json);
                }
            }
        }
        catch (OperationCanceledException) { /* expected when the probe CTS fires */ }
        catch (IOException ex) when (ex.InnerException is OperationCanceledException) { /* TestServer wraps OCE as IOException */ }
        return null;
    }

    [Fact]
    public async Task AC1_open_stream_then_start_work_item_emits_raised_within_2s()
    {
        using var streamCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (resp, reader) = await OpenStreamAsync(streamCts.Token);
        using var _ = resp;
        using var __ = reader;

        // Small delay so the stream is definitely subscribed before the transition.
        await Task.Delay(200, streamCts.Token);

        var workItemId = await _operator.StartWorkItemAsync(_projectId);

        using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(streamCts.Token, waitCts.Token);
        var ev = await AwaitFirstEventAsync(reader, linked.Token);

        ev.Should().NotBeNull();
        ev!.Value.GetProperty("kind").GetString().Should().Be("raised");
        ev.Value.GetProperty("workItemId").GetGuid().Should().Be(workItemId);
    }

    [Fact]
    public async Task AC2_signal_resolution_emits_dismissed()
    {
        // Seed a work item with a pending row BEFORE opening the stream — we want to test the
        // dismiss-on-signal path specifically, not the raise event.
        var workItemId = await _operator.StartWorkItemAsync(_projectId);

        using var streamCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (resp, reader) = await OpenStreamAsync(streamCts.Token);
        using var _ = resp;
        using var __ = reader;
        await Task.Delay(200, streamCts.Token);

        // Resolve via signal — reconciler should publish a dismissed event.
        var sig = await _operator.PostAsJsonAsync(
            $"/api/projects/{_projectId}/work-items/{workItemId}/checkpoints/approve/signal",
            new { outcome = "approve" });
        sig.EnsureSuccessStatusCode();

        using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(streamCts.Token, waitCts.Token);
        var ev = await AwaitFirstEventAsync(reader, linked.Token);

        ev.Should().NotBeNull();
        ev!.Value.GetProperty("kind").GetString().Should().Be("dismissed");
        ev.Value.GetProperty("workItemId").GetGuid().Should().Be(workItemId);
    }

    [Fact]
    public async Task AC5_fresh_subscriber_only_sees_events_after_subscription()
    {
        // Trigger a transition BEFORE any stream is open. This event has no subscriber and is
        // discarded (the registry has no in-memory replay queue).
        var firstWorkItemId = await _operator.StartWorkItemAsync(_projectId);

        // Now open the stream and trigger a second transition. The subscriber must see the
        // second event — and ONLY the second (not a replayed first).
        using var streamCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (resp, reader) = await OpenStreamAsync(streamCts.Token);
        using var _ = resp;
        using var __ = reader;
        await Task.Delay(200, streamCts.Token);

        var secondWorkItemId = await _operator.StartWorkItemAsync(_projectId);

        using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(streamCts.Token, waitCts.Token);
        var ev = await AwaitFirstEventAsync(reader, linked.Token);

        ev.Should().NotBeNull();
        // The event must be for the SECOND work item — not a replay of the first.
        ev!.Value.GetProperty("workItemId").GetGuid().Should().Be(secondWorkItemId);
        ev.Value.GetProperty("workItemId").GetGuid().Should().NotBe(firstWorkItemId);
    }
}
