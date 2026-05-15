namespace DevHub.Contracts.ApplicationErrors;

public sealed class ExecutorFailureException(string executorKey, string detail, string title = "Executor failure")
    : DomainException(title, detail, status: 502, type: "/probs/executor-failure")
{
    public string ExecutorKey { get; } = executorKey;
}
