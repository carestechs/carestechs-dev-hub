using System.Text.Json;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Pagination;
using DevHub.Modules.Audit.DTOs;
using DevHub.Modules.Audit.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Audit.Services;

internal sealed class AuditQueryService(
    AuditDbContext db,
    IMemberLookup members) : IAuditQueryService
{
    public Task<PagedEnvelopeDto<AuditEntryDto>> ListForProjectAsync(
        Guid projectId, AuditFilter filter, PageRequest page, CancellationToken cancellationToken = default)
    {
        var scoped = filter with { ProjectId = projectId };
        return ListInternalAsync(scoped, page, scopeProject: true, cancellationToken);
    }

    public Task<PagedEnvelopeDto<AuditEntryDto>> ListAsync(
        AuditFilter filter, PageRequest page, CancellationToken cancellationToken = default) =>
        ListInternalAsync(filter, page, scopeProject: false, cancellationToken);

    private async Task<PagedEnvelopeDto<AuditEntryDto>> ListInternalAsync(
        AuditFilter filter, PageRequest page, bool scopeProject, CancellationToken ct)
    {
        IQueryable<AuditEntry> q = db.AuditEntries.AsNoTracking();

        if (scopeProject)
        {
            // The project-scoped query MUST constrain to one project; ignore caller's ProjectId
            // when it conflicts with the scope (the route already pinned it).
            q = q.Where(a => a.ProjectId == filter.ProjectId);
        }
        else if (filter.ProjectId is { } pid)
        {
            q = q.Where(a => a.ProjectId == pid);
        }

        if (filter.ActingMemberId is { } amid) q = q.Where(a => a.ActingMemberId == amid);
        if (!string.IsNullOrWhiteSpace(filter.TargetType)) q = q.Where(a => a.TargetType == filter.TargetType);
        if (!string.IsNullOrWhiteSpace(filter.Action)) q = q.Where(a => a.Action == filter.Action);
        if (filter.Outcome is { } outcome) q = q.Where(a => a.Outcome == outcome);
        if (filter.From is { } from) q = q.Where(a => a.OccurredAt >= from);
        if (filter.To is { } to) q = q.Where(a => a.OccurredAt <= to);

        var totalCount = await q.CountAsync(ct);

        // Default ordering: newest first. SortBy/SortDir from PageRequest are honored for
        // forward-compat but v1 supports only occurredAt-desc and occurredAt-asc.
        q = (page.SortBy?.ToLowerInvariant(), page.SortDir?.ToLowerInvariant()) switch
        {
            ("occurredat", "asc") => q.OrderBy(a => a.OccurredAt),
            _                     => q.OrderByDescending(a => a.OccurredAt),
        };

        var rows = await q
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(ct);

        var memberIds = rows.Where(r => r.ActingMemberId is not null)
            .Select(r => r.ActingMemberId!.Value).Distinct().ToList();
        var memberMap = new Dictionary<Guid, AuditActorDto>();
        foreach (var id in memberIds)
        {
            var m = await members.FindByIdAsync(id, ct);
            memberMap[id] = m is null
                ? new AuditActorDto(id, "(unknown)")
                : new AuditActorDto(m.Id, m.DisplayName);
        }

        var dtos = rows.Select(r => new AuditEntryDto(
            r.Id, r.OccurredAt,
            r.ActingMemberId is { } amid ? memberMap[amid] : null,
            r.ProjectId,
            r.TargetType, r.TargetId, r.Action, r.Outcome,
            r.Reason,
            ParseDetails(r.DetailsJson))).ToList();

        return new PagedEnvelopeDto<AuditEntryDto>(
            dtos,
            new PageMeta(totalCount, page.Page, page.PageSize, page.SortBy, page.SortDir));
    }

    private static JsonElement? ParseDetails(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }
}
