using DevHub.Contracts.Executors;
using DevHub.Modules.WorkItems.Services.Orchestrator;

namespace DevHub.Modules.WorkItems.Services;

internal sealed class ExecutorClientFactory(
    ExecutorHttpClient devhubClient,
    OrchestratorExecutorClient orchestratorClient) : IExecutorClientFactory
{
    public IExecutorHttpClient Resolve(ExecutorRegistrationDescriptor descriptor) =>
        descriptor.Protocol switch
        {
            "orchestrator" => orchestratorClient,
            _ => devhubClient,  // "devhub" + any unrecognized value falls back to legacy
        };
}
