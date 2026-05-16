namespace DevHub.TestHarness.FakeExecutor;

/// One record per HTTP call hitting the fake executor.
public sealed record CallRecord(
    string Method,
    string Path,
    string CorrelationMarker,
    string? Authorization,
    string? BodyJson,
    DateTimeOffset OccurredAt);
