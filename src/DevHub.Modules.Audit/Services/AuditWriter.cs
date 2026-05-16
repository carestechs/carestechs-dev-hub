using System.Text.Json;
using DevHub.Contracts.Audit;
using DevHub.Modules.Audit.Entities;

namespace DevHub.Modules.Audit.Services;

/// <summary>
/// Stages an <see cref="AuditEntry"/> insert against the audit DbContext. If the caller
/// has already opened a transaction, we only Add (the caller's SaveChangesAsync commits
/// both rows atomically). With no outer transaction we SaveChangesAsync ourselves.
/// </summary>
internal sealed class AuditWriter(AuditDbContext db) : IAuditWriter
{
    public async Task WriteAsync(AuditWriteRequest request, CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntry
        {
            OccurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow,
            ActingMemberId = request.ActingMemberId,
            ProjectId = request.ProjectId,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            Action = request.Action,
            Outcome = request.Outcome,
            Reason = request.Reason,
            DetailsJson = request.Details is null ? null : JsonSerializer.Serialize(request.Details),
        };
        db.AuditEntries.Add(entry);
        if (db.Database.CurrentTransaction is null)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
