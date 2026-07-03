using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Contracts.Workspace;
using DevHub.Modules.Workspace.Domain;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Workspace.Services;

public interface IProjectDocsService
{
    Task<IReadOnlyList<ProjectDocSummaryDto>> ListAsync(Guid projectId, Guid callerMemberId, CancellationToken ct);
    Task<ProjectDocDto> GetAsync(Guid projectId, string key, Guid callerMemberId, CancellationToken ct);
    Task<ProjectDocDto> UpsertAsync(Guid projectId, string key, string content, Guid actingMemberId, CancellationToken ct);
}

internal sealed class ProjectDocsService(
    WorkspaceDbContext db,
    IProjectAuthorizationService authz,
    IAuditWriter audit) : IProjectDocsService, IProjectDocsQuery
{
    public async Task<IReadOnlyList<ProjectDocSummaryDto>> ListAsync(
        Guid projectId, Guid callerMemberId, CancellationToken ct)
    {
        await authz.EnsureAuthorizedAsync(callerMemberId, projectId, "project:read", requiredRoleKey: null, ct);

        var rows = await db.ProjectDocs
            .AsNoTracking()
            .Where(d => d.ProjectId == projectId)
            .ToListAsync(ct);

        var memberIds = rows.Where(r => r.UpdatedById.HasValue).Select(r => r.UpdatedById!.Value).Distinct().ToList();
        var memberNames = await db.Members
            .AsNoTracking()
            .Where(m => memberIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.DisplayName, ct);

        return ProjectDocKeys.All.Select(meta =>
        {
            var row = rows.FirstOrDefault(r => r.DocKey == meta.Key);
            var filled = IsFilled(row?.Content);
            return new ProjectDocSummaryDto(
                meta.Key,
                meta.Label,
                meta.Description,
                filled,
                filled ? row!.UpdatedAt : null,
                row?.UpdatedById is { } uid ? memberNames.GetValueOrDefault(uid) : null);
        }).ToList();
    }

    public async Task<ProjectDocDto> GetAsync(
        Guid projectId, string key, Guid callerMemberId, CancellationToken ct)
    {
        ValidateKey(key);
        await authz.EnsureAuthorizedAsync(callerMemberId, projectId, "project:read", requiredRoleKey: null, ct);

        var row = await db.ProjectDocs.AsNoTracking()
            .FirstOrDefaultAsync(d => d.ProjectId == projectId && d.DocKey == key, ct);

        var meta = ProjectDocKeys.All.First(m => m.Key == key);
        var filled = IsFilled(row?.Content);
        string? updatedByName = null;
        if (row?.UpdatedById is { } uid)
            updatedByName = await db.Members.AsNoTracking()
                .Where(m => m.Id == uid)
                .Select(m => m.DisplayName)
                .FirstOrDefaultAsync(ct);

        return new ProjectDocDto(
            meta.Key, meta.Label, meta.Description, meta.Hints,
            row?.Content, filled,
            filled ? row!.UpdatedAt : null,
            updatedByName);
    }

    public async Task<ProjectDocDto> UpsertAsync(
        Guid projectId, string key, string content, Guid actingMemberId, CancellationToken ct)
    {
        ValidateKey(key);
        await authz.EnsureOperatorAsync(actingMemberId, "project:doc-update", ct);

        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw new NotFoundException("Project not found.");

        var normalised = NormaliseContent(content);

        var row = await db.ProjectDocs.FirstOrDefaultAsync(
            d => d.ProjectId == projectId && d.DocKey == key, ct);

        if (row is null)
        {
            row = new ProjectDoc { ProjectId = projectId, DocKey = key, Content = normalised, UpdatedById = actingMemberId };
            db.ProjectDocs.Add(row);
        }
        else
        {
            row.Content = normalised;
            row.UpdatedById = actingMemberId;
        }

        await audit.WriteAsync(new AuditWriteRequest("ProjectDoc", row.Id, "project:doc-updated", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            ProjectId = projectId,
            Details = new Dictionary<string, object?> { ["docKey"] = key, ["filled"] = IsFilled(normalised) },
        }, ct);

        await db.SaveChangesAsync(ct);

        return await GetAsync(projectId, key, actingMemberId, ct);
    }

    private static void ValidateKey(string key)
    {
        if (!ProjectDocKeys.IsValid(key))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(key)] = [$"Unknown document key '{key}'. Valid keys: {string.Join(", ", ProjectDocKeys.ValidKeys)}"]
            });
    }

    private static string? NormaliseContent(string content) =>
        string.IsNullOrWhiteSpace(content) ? null : content.Trim();

    public async Task<(bool AllFilled, IReadOnlyList<string> MissingKeys)> CheckAllFilledAsync(
        Guid projectId, CancellationToken ct)
    {
        var filledKeys = await db.ProjectDocs
            .AsNoTracking()
            .Where(d => d.ProjectId == projectId && d.Content != null && d.Content != "")
            .Select(d => d.DocKey)
            .ToListAsync(ct);

        var missing = ProjectDocKeys.All
            .Select(m => m.Key)
            .Where(k => !filledKeys.Contains(k))
            .ToList();

        return (missing.Count == 0, missing);
    }

    internal static bool IsFilled(string? content) =>
        !string.IsNullOrWhiteSpace(content);
}
