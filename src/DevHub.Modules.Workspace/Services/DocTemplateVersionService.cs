using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Workspace.Services;

public interface IDocTemplateVersionService
{
    Task<IReadOnlyList<DocTemplateVersionDto>> ListAsync(Guid callerMemberId, CancellationToken ct);
    Task<DocTemplateVersionDto> CreateAsync(Guid sourceVersionId, string? notes, Guid actingMemberId, CancellationToken ct);
    Task<DocTemplateVersionDto> ActivateAsync(Guid id, Guid actingMemberId, CancellationToken ct);
}

internal sealed class DocTemplateVersionService(
    WorkspaceDbContext db,
    IProjectAuthorizationService authz,
    IAuditWriter audit) : IDocTemplateVersionService
{
    public async Task<IReadOnlyList<DocTemplateVersionDto>> ListAsync(Guid callerMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(callerMemberId, "doc-template:list", ct);

        var versions = await db.DocTemplateVersions
            .AsNoTracking()
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);

        var versionIds = versions.Select(v => v.Id).ToList();

        var sectionCounts = await db.DocTemplateSections
            .AsNoTracking()
            .Where(s => versionIds.Contains(s.VersionId))
            .GroupBy(s => s.VersionId)
            .Select(g => new { VersionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VersionId, x => x.Count, ct);

        var projectCounts = await db.Projects
            .AsNoTracking()
            .Where(p => versionIds.Contains(p.DocTemplateVersionId))
            .GroupBy(p => p.DocTemplateVersionId)
            .Select(g => new { VersionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VersionId, x => x.Count, ct);

        return versions.Select(v => new DocTemplateVersionDto(
            v.Id, v.VersionNumber, v.IsActive, v.Notes,
            sectionCounts.GetValueOrDefault(v.Id, 0),
            projectCounts.GetValueOrDefault(v.Id, 0),
            v.CreatedAt)).ToList();
    }

    public async Task<DocTemplateVersionDto> CreateAsync(
        Guid sourceVersionId, string? notes, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "doc-template:create", ct);

        var sourceSections = await db.DocTemplateSections
            .AsNoTracking()
            .Where(s => s.VersionId == sourceVersionId)
            .ToListAsync(ct);

        if (sourceSections.Count == 0)
            throw new NotFoundException($"Source template version '{sourceVersionId}' not found or has no sections.");

        var maxVersion = await db.DocTemplateVersions
            .AsNoTracking()
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;

        var newVersion = new DocTemplateVersion
        {
            VersionNumber = maxVersion + 1,
            IsActive = false,
            Notes = notes,
        };
        db.DocTemplateVersions.Add(newVersion);
        await db.SaveChangesAsync(ct);

        var newSections = sourceSections.Select(s => new DocTemplateSection
        {
            VersionId = newVersion.Id,
            DocKey = s.DocKey,
            SectionKey = s.SectionKey,
            Label = s.Label,
            Hint = s.Hint,
            Required = s.Required,
            DisplayOrder = s.DisplayOrder,
        }).ToList();
        db.DocTemplateSections.AddRange(newSections);

        await audit.WriteAsync(new AuditWriteRequest("DocTemplateVersion", newVersion.Id, "doc-template:created", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            Details = new Dictionary<string, object?>
            {
                ["versionNumber"] = newVersion.VersionNumber,
                ["sourceVersionId"] = sourceVersionId,
            },
        }, ct);

        await db.SaveChangesAsync(ct);

        return new DocTemplateVersionDto(
            newVersion.Id, newVersion.VersionNumber, newVersion.IsActive, newVersion.Notes,
            newSections.Count, 0, newVersion.CreatedAt);
    }

    public async Task<DocTemplateVersionDto> ActivateAsync(Guid id, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "doc-template:activate", ct);

        var target = await db.DocTemplateVersions.FindAsync([id], ct)
            ?? throw new NotFoundException($"Template version '{id}' not found.");

        if (target.IsActive)
            return await ToDto(target, ct);

        // Atomic swap: deactivate all, activate target.
        await db.DocTemplateVersions
            .Where(v => v.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsActive, false), ct);

        target.IsActive = true;

        await audit.WriteAsync(new AuditWriteRequest("DocTemplateVersion", id, "doc-template:activated", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            Details = new Dictionary<string, object?> { ["versionNumber"] = target.VersionNumber },
        }, ct);

        await db.SaveChangesAsync(ct);
        return await ToDto(target, ct);
    }

    private async Task<DocTemplateVersionDto> ToDto(DocTemplateVersion v, CancellationToken ct)
    {
        var sectionCount = await db.DocTemplateSections.CountAsync(s => s.VersionId == v.Id, ct);
        var projectCount = await db.Projects.CountAsync(p => p.DocTemplateVersionId == v.Id, ct);
        return new DocTemplateVersionDto(v.Id, v.VersionNumber, v.IsActive, v.Notes, sectionCount, projectCount, v.CreatedAt);
    }
}
