using DevHub.Contracts.Executors;

namespace DevHub.Modules.WorkItems.Services;

/// <summary>
/// FEAT-010: picks the right <see cref="IExecutorHttpClient"/> implementation for an
/// executor registration based on <see cref="ExecutorRegistrationDescriptor.Protocol"/>.
///
/// Today's values: <c>"devhub"</c> (default; existing protocol — FakeExecutor compatible)
/// and <c>"orchestrator"</c> (carestechs-agent-orchestrator's <c>/api/v1/runs</c> API).
/// Any unrecognized value falls back to <c>"devhub"</c> — graceful default preserves
/// backwards compatibility.
/// </summary>
public interface IExecutorClientFactory
{
    IExecutorHttpClient Resolve(ExecutorRegistrationDescriptor descriptor);
}
