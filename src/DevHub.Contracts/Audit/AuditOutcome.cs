namespace DevHub.Contracts.Audit;

/// <summary>
/// Result of an audited action. Mirrors data-model.md §AuditEntry.outcome.
/// </summary>
public enum AuditOutcome
{
    /// Authorization succeeded and the action was applied / forwarded.
    Granted = 0,

    /// Authorization failed; the action did not reach the executor / downstream system.
    Denied = 1,

    /// Authorized but the action itself returned an error (e.g. an executor 5xx).
    Failed = 2,
}
