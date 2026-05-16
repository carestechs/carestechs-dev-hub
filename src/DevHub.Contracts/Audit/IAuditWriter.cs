namespace DevHub.Contracts.Audit;

/// <summary>
/// Append-only audit sink consumed by every module's services. The implementation
/// participates in the caller's open transaction when one exists, so a mutation and
/// its audit entry commit (or roll back) atomically.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditWriteRequest request, CancellationToken cancellationToken = default);
}
