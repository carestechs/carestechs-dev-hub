using DevHub.Contracts.Pagination;
using DevHub.Modules.Audit.DTOs;

namespace DevHub.Modules.Audit.Services;

public interface IAuditQueryService
{
    Task<PagedEnvelopeDto<AuditEntryDto>> ListForProjectAsync(
        Guid projectId, AuditFilter filter, PageRequest page, CancellationToken cancellationToken = default);

    Task<PagedEnvelopeDto<AuditEntryDto>> ListAsync(
        AuditFilter filter, PageRequest page, CancellationToken cancellationToken = default);
}
