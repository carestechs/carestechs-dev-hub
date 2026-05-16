namespace DevHub.Contracts.ApplicationErrors;

public sealed class ExecutorFailureException(
    Guid executorId,
    string executorKey,
    string correlationId,
    int? upstreamStatus,
    string? upstreamBody,
    string detail = "Executor refused or was unreachable.")
    : DomainException("Executor failure", detail, status: 502, type: "/probs/executor-failure")
{
    public Guid ExecutorId { get; } = executorId;
    public string ExecutorKey { get; } = executorKey;
    public string CorrelationId { get; } = correlationId;
    public int? UpstreamStatus { get; } = upstreamStatus;
    public string? UpstreamBody { get; } = upstreamBody;
}
