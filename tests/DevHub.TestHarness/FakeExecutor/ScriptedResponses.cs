namespace DevHub.TestHarness.FakeExecutor;

/// Test-tunable response shapes. Defaults model a feature-delivery-ish executor that goes
/// from <c>Running</c> to <c>WaitingOnCheckpoint(approve)</c> after start, then to
/// <c>Completed</c> after an <c>approve</c> signal.
public sealed class ScriptedResponses
{
    public string StartStatus { get; set; } = "WaitingOnCheckpoint";
    public string? StartCheckpointKey { get; set; } = "approve";
    public object StartExecutorState { get; set; } = new
    {
        steps = new[]
        {
            new { key = "brief", label = "Brief" },
            new { key = "approve", label = "Approve change" },
        },
    };

    public string FetchStatus { get; set; } = "WaitingOnCheckpoint";
    public string? FetchCheckpointKey { get; set; } = "approve";
    public object FetchExecutorState { get; set; } = new { };

    public string SignalStatus { get; set; } = "Completed";
    public string? SignalCheckpointKey { get; set; } = null;
    public object SignalExecutorState { get; set; } = new { };
    public int SignalHttpStatusCode { get; set; } = 200;

    /// Chunks for the SSE endpoint. Each entry is one byte payload + a delay before the next
    /// write. Default: three "data: ..." chunks with a 50ms delay each.
    public IList<(string Chunk, TimeSpan DelayBefore)> StreamChunks { get; set; } = new List<(string, TimeSpan)>
    {
        ("data: tick-1\n\n", TimeSpan.Zero),
        ("data: tick-2\n\n", TimeSpan.FromMilliseconds(50)),
        ("data: tick-3\n\n", TimeSpan.FromMilliseconds(50)),
    };
}
