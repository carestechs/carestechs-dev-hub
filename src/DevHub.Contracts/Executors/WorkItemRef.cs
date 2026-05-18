namespace DevHub.Contracts.Executors;

/// <summary>
/// Identifies a work item to <see cref="IExecutorHttpClient"/> implementations
/// (FEAT-010 / T-086).
///
/// <see cref="Marker"/> is DevHub's stable id — the <c>ExecutorCorrelationMarker</c>
/// on the WorkItem row. It's what DevHub-protocol executors use in their URL paths
/// (<c>/work-items/{marker}/...</c>).
///
/// <see cref="ExecutorRunId"/> is the orchestrator's run uuid. Populated by the
/// orchestrator client after Start succeeds; null before that and for DevHub-protocol
/// executors that never surface one.
/// </summary>
public sealed record WorkItemRef(string Marker, Guid? ExecutorRunId);
