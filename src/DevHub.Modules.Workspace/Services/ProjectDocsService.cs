using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Contracts.Workspace;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevHub.Modules.Workspace.Services;

public interface IProjectDocsService
{
    Task<IReadOnlyList<ProjectDocSummaryDto>> ListAsync(Guid projectId, Guid callerMemberId, CancellationToken ct);
    Task<ProjectDocDto> GetAsync(Guid projectId, string docKey, Guid callerMemberId, CancellationToken ct);
    Task<ProjectDocDto> UpsertDocSectionsAsync(Guid projectId, string docKey, Dictionary<string, string> sections, Guid actingMemberId, CancellationToken ct);
}

internal sealed class ProjectDocsService(
    WorkspaceDbContext db,
    IProjectAuthorizationService authz,
    IAuditWriter audit,
    IGitHubService gitHub,
    ILogger<ProjectDocsService> logger) : IProjectDocsService, IProjectDocsQuery, IProjectDocSyncService
{
    // Maps doc key → repo file path. CLAUDE.md goes to the repo root; all others under docs/.
    private static readonly IReadOnlyDictionary<string, string> RepoFilePaths =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["stakeholder-definition"] = "docs/stakeholder-definition.md",
            ["architecture"]           = "docs/ARCHITECTURE.md",
            ["data-model"]             = "docs/data-model.md",
            ["api-spec"]               = "docs/api-spec.md",
            ["ui-specification"]       = "docs/ui-specification.md",
            ["primary-user-persona"]   = "docs/personas/primary-user.md",
            ["claude-md"]              = "CLAUDE.md",
        };
    // Stable presentation metadata per doc key — labels and descriptions never stored in DB.
    private static readonly IReadOnlyDictionary<string, (string Label, string Description)> DocMeta =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["stakeholder-definition"] = ("Stakeholder Definition",  "Why this project exists, who it's for, and what is firmly out of scope."),
            ["architecture"]           = ("Architecture",            "System structure — modules, services, and how data flows between them."),
            ["data-model"]             = ("Data Model",              "Entities, fields, relationships, and business constraints."),
            ["api-spec"]               = ("API Specification",       "Endpoint contracts — routes, request/response shapes, and status codes."),
            ["ui-specification"]       = ("UI Specification",        "Screens, components, interactions, and visual states."),
            ["primary-user-persona"]   = ("Primary User Persona",    "Who the primary user is — their goals, context, and pain points."),
            ["claude-md"]              = ("CLAUDE.md",               "Implementation conventions, patterns to follow, and anti-patterns to avoid."),
        };

    public async Task<IReadOnlyList<ProjectDocSummaryDto>> ListAsync(
        Guid projectId, Guid callerMemberId, CancellationToken ct)
    {
        await authz.EnsureAuthorizedAsync(callerMemberId, projectId, "project:read", requiredRoleKey: null, ct);

        var versionId = await GetVersionIdAsync(projectId, ct);

        var sections = await db.DocTemplateSections
            .AsNoTracking()
            .Where(s => s.VersionId == versionId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(ct);

        var filledSectionIds = await db.ProjectDocSections
            .AsNoTracking()
            .Where(ps => ps.ProjectId == projectId && ps.Content != null && ps.Content != "")
            .Select(ps => ps.SectionId)
            .ToHashSetAsync(ct);

        var docKeys = sections.Select(s => s.DocKey).Distinct().ToList();

        var allRequired = sections.Where(s => s.Required).ToList();
        var locked = allRequired.Count > 0 && allRequired.All(s => filledSectionIds.Contains(s.Id));

        return docKeys.Select(docKey =>
        {
            var docSections = sections.Where(s => s.DocKey == docKey).ToList();
            var requiredSections = docSections.Where(s => s.Required).ToList();
            var filled = requiredSections.Count > 0 && requiredSections.All(s => filledSectionIds.Contains(s.Id));
            var filledCount = docSections.Count(s => filledSectionIds.Contains(s.Id));

            var (label, description) = DocMeta.GetValueOrDefault(docKey, (docKey, ""));

            var sectionSummaries = docSections
                .Select(s => new ProjectDocSectionSummaryDto(s.SectionKey, s.Label, s.Required, filledSectionIds.Contains(s.Id)))
                .ToList();

            return new ProjectDocSummaryDto(docKey, label, description, filled, locked, filledCount, docSections.Count, sectionSummaries);
        }).ToList();
    }

    public async Task<ProjectDocDto> GetAsync(
        Guid projectId, string docKey, Guid callerMemberId, CancellationToken ct)
    {
        await authz.EnsureAuthorizedAsync(callerMemberId, projectId, "project:read", requiredRoleKey: null, ct);

        var versionId = await GetVersionIdAsync(projectId, ct);

        var sections = await db.DocTemplateSections
            .AsNoTracking()
            .Where(s => s.VersionId == versionId && s.DocKey == docKey)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(ct);

        if (sections.Count == 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["key"] = [$"Unknown document key '{docKey}'."]
            });

        var sectionIds = sections.Select(s => s.Id).ToList();
        var contentRows = await db.ProjectDocSections
            .AsNoTracking()
            .Where(ps => ps.ProjectId == projectId && sectionIds.Contains(ps.SectionId))
            .ToListAsync(ct);

        var updaterIds = contentRows.Where(c => c.UpdatedById.HasValue).Select(c => c.UpdatedById!.Value).Distinct().ToList();
        var updaterNames = updaterIds.Count > 0
            ? await db.Members.AsNoTracking()
                .Where(m => updaterIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.DisplayName, ct)
            : new Dictionary<Guid, string>();

        var contentBySectionId = contentRows.ToDictionary(c => c.SectionId);

        var sectionDtos = sections.Select(s =>
        {
            contentBySectionId.TryGetValue(s.Id, out var row);
            var isFilled = IsFilled(row?.Content);
            return new ProjectDocSectionDto(
                s.SectionKey, s.Label, s.Hint, s.Required,
                row?.Content, isFilled,
                isFilled ? row!.UpdatedAt : null,
                row?.UpdatedById is { } uid ? updaterNames.GetValueOrDefault(uid) : null);
        }).ToList();

        var allRequired = sections.Where(s => s.Required).ToList();
        var docFilled = allRequired.Count > 0 && allRequired.All(s =>
            contentBySectionId.TryGetValue(s.Id, out var r) && IsFilled(r.Content));

        // locked = all required sections across ALL docs are filled
        var allVersionRequired = await db.DocTemplateSections
            .AsNoTracking()
            .Where(s => s.VersionId == versionId && s.Required)
            .Select(s => s.Id)
            .ToListAsync(ct);
        var allFilledIds = await db.ProjectDocSections
            .AsNoTracking()
            .Where(ps => ps.ProjectId == projectId && ps.Content != null && ps.Content != "")
            .Select(ps => ps.SectionId)
            .ToHashSetAsync(ct);
        var locked = allVersionRequired.Count > 0 && allVersionRequired.All(id => allFilledIds.Contains(id));

        var (label, description) = DocMeta.GetValueOrDefault(docKey, (docKey, ""));
        return new ProjectDocDto(docKey, label, description, docFilled, locked, sectionDtos);
    }

    public async Task<ProjectDocDto> UpsertDocSectionsAsync(
        Guid projectId, string docKey, Dictionary<string, string> sections, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "project:doc-update", ct);

        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw new NotFoundException("Project not found.");

        var versionId = await GetVersionIdAsync(projectId, ct);

        // Enforce lock: once all required sections are filled, docs are read-only.
        var allVersionRequired = await db.DocTemplateSections
            .AsNoTracking()
            .Where(s => s.VersionId == versionId && s.Required)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (allVersionRequired.Count > 0)
        {
            var filledCount = await db.ProjectDocSections
                .AsNoTracking()
                .CountAsync(ps => ps.ProjectId == projectId
                    && allVersionRequired.Contains(ps.SectionId)
                    && ps.Content != null && ps.Content != "", ct);
            if (filledCount >= allVersionRequired.Count)
                throw new ProjectDocsLockedException();
        }

        var templateSections = await db.DocTemplateSections
            .Where(s => s.VersionId == versionId && s.DocKey == docKey)
            .ToListAsync(ct);

        if (templateSections.Count == 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["key"] = [$"Unknown document key '{docKey}'."]
            });

        var templateByKey = templateSections.ToDictionary(s => s.SectionKey, StringComparer.Ordinal);

        var unknownKeys = sections.Keys.Where(k => !templateByKey.ContainsKey(k)).ToList();
        if (unknownKeys.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["sections"] = [$"Unknown section key(s): {string.Join(", ", unknownKeys)}"]
            });

        var templateSectionIds = templateSections.Select(s => s.Id).ToList();
        var existingRows = await db.ProjectDocSections
            .Where(ps => ps.ProjectId == projectId && templateSectionIds.Contains(ps.SectionId))
            .ToListAsync(ct);
        var existingBySectionId = existingRows.ToDictionary(r => r.SectionId);

        foreach (var (key, value) in sections)
        {
            var section = templateByKey[key];
            var normalised = NormaliseContent(value);

            if (existingBySectionId.TryGetValue(section.Id, out var existing))
            {
                existing.Content = normalised;
                existing.UpdatedById = actingMemberId;
            }
            else
            {
                db.ProjectDocSections.Add(new ProjectDocSection
                {
                    ProjectId = projectId,
                    SectionId = section.Id,
                    Content = normalised,
                    UpdatedById = actingMemberId,
                });
            }
        }

        await audit.WriteAsync(new AuditWriteRequest("ProjectDocSection", projectId, "project:doc-updated", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            ProjectId = projectId,
            Details = new Dictionary<string, object?> { ["docKey"] = docKey, ["sectionCount"] = sections.Count },
        }, ct);

        await db.SaveChangesAsync(ct);

        // Detect lock transition: if this save caused all required sections to become filled
        // for the first time, push all docs to the repo.
        bool? repoSynced = null;
        var filledAfter = await db.ProjectDocSections
            .AsNoTracking()
            .CountAsync(ps => ps.ProjectId == projectId
                && allVersionRequired.Contains(ps.SectionId)
                && ps.Content != null && ps.Content != "", ct);
        if (allVersionRequired.Count > 0 && filledAfter >= allVersionRequired.Count)
        {
            var pushResult = await PushAllDocsToRepoAsync(projectId, ct);
            repoSynced = pushResult; // null = skipped (no repo), true = ok, false = failed
            if (pushResult == true)
            {
                await audit.WriteAsync(new AuditWriteRequest("ProjectDocSection", projectId, "project:docs-repo-synced", AuditOutcome.Granted)
                {
                    ProjectId = projectId,
                    Details = new Dictionary<string, object?> { ["trigger"] = "lock-transition" },
                }, ct);
            }
        }

        var dto = await GetAsync(projectId, docKey, actingMemberId, ct);
        return dto with { RepoSynced = repoSynced };
    }

    public async Task<(bool AllFilled, IReadOnlyList<string> MissingKeys)> CheckAllFilledAsync(
        Guid projectId, CancellationToken ct)
    {
        var versionId = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.DocTemplateVersionId)
            .FirstOrDefaultAsync(ct);

        if (versionId is null || versionId == Guid.Empty)
            return (false, []);

        var requiredSections = await db.DocTemplateSections
            .AsNoTracking()
            .Where(s => s.VersionId == versionId && s.Required)
            .Select(s => new { s.Id, s.DocKey })
            .ToListAsync(ct);

        var filledSectionIds = await db.ProjectDocSections
            .AsNoTracking()
            .Where(ps => ps.ProjectId == projectId && ps.Content != null && ps.Content != "")
            .Select(ps => ps.SectionId)
            .ToHashSetAsync(ct);

        var missingDocKeys = requiredSections
            .Where(s => !filledSectionIds.Contains(s.Id))
            .Select(s => s.DocKey)
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        return (missingDocKeys.Count == 0, missingDocKeys);
    }

    // -------------------------------------------------------------------------
    // IProjectDocSyncService
    // -------------------------------------------------------------------------

    public async Task<bool?> PushAllDocsToRepoAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Repo, p.DefaultBranch, p.DocTemplateVersionId })
            .FirstOrDefaultAsync(ct);

        if (project is null || string.IsNullOrWhiteSpace(project.Repo) || string.IsNullOrWhiteSpace(project.DefaultBranch))
        {
            logger.LogDebug("Skipping repo push for project {ProjectId}: repo or defaultBranch not set.", projectId);
            return null;
        }

        var sections = await db.DocTemplateSections
            .AsNoTracking()
            .Where(s => s.VersionId == project.DocTemplateVersionId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(ct);

        var contentBySectionId = await db.ProjectDocSections
            .AsNoTracking()
            .Where(ps => ps.ProjectId == projectId)
            .ToDictionaryAsync(ps => ps.SectionId, ps => ps.Content, ct);

        var docKeys = sections.Select(s => s.DocKey).Distinct().ToList();

        var anyFailure = false;
        foreach (var docKey in docKeys)
        {
            var docSections = sections
                .Where(s => s.DocKey == docKey)
                .Select(s => (s.Label, contentBySectionId.GetValueOrDefault(s.Id)))
                .ToList();

            var (docLabel, _) = DocMeta.GetValueOrDefault(docKey, (docKey, ""));
            var markdown = AssembleDocMarkdown(docLabel, docSections!);

            if (!RepoFilePaths.TryGetValue(docKey, out var filePath))
            {
                logger.LogWarning("No repo file path mapping for doc key '{DocKey}'; skipping.", docKey);
                continue;
            }

            try
            {
                await gitHub.UpsertFileAsync(
                    project.Repo, filePath, markdown, project.DefaultBranch,
                    $"docs: update {docKey} via DevHub", ct);
            }
            catch (Exception ex)
            {
                anyFailure = true;
                logger.LogWarning(ex, "GitHub push failed for doc '{DocKey}' on project {ProjectId}.", docKey, projectId);
                await audit.WriteAsync(new AuditWriteRequest("ProjectDocSection", projectId, "project:docs-repo-synced", AuditOutcome.Failed)
                {
                    ProjectId = projectId,
                    Details = new Dictionary<string, object?> { ["docKey"] = docKey, ["error"] = ex.Message },
                }, ct);
            }
        }

        return !anyFailure;
    }

    public async Task<bool?> PullDocsFromRepoAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Repo, p.DefaultBranch, p.DocTemplateVersionId })
            .FirstOrDefaultAsync(ct);

        if (project is null || string.IsNullOrWhiteSpace(project.Repo) || string.IsNullOrWhiteSpace(project.DefaultBranch))
        {
            logger.LogDebug("Skipping repo pull for project {ProjectId}: repo or defaultBranch not set.", projectId);
            return null;
        }

        var templateSections = await db.DocTemplateSections
            .AsNoTracking()
            .Where(s => s.VersionId == project.DocTemplateVersionId)
            .ToListAsync(ct);

        var anyFailure = false;

        foreach (var (docKey, filePath) in RepoFilePaths)
        {
            try
            {
                var markdown = await gitHub.GetFileContentAsync(project.Repo, filePath, project.DefaultBranch, ct);
                if (markdown is null)
                {
                    logger.LogDebug("File '{FilePath}' not found in repo for project {ProjectId}; skipping.", filePath, projectId);
                    continue;
                }

                var docSections = templateSections.Where(s => s.DocKey == docKey).ToList();
                var parsed = ParseDocMarkdown(markdown, docSections);

                if (parsed.Count == 0) continue;

                var sectionIds = parsed.Keys.ToList();
                var existingRows = await db.ProjectDocSections
                    .Where(ps => ps.ProjectId == projectId && sectionIds.Contains(ps.SectionId))
                    .ToListAsync(ct);
                var existingBySectionId = existingRows.ToDictionary(r => r.SectionId);

                foreach (var (sectionId, content) in parsed)
                {
                    if (existingBySectionId.TryGetValue(sectionId, out var existing))
                        existing.Content = NormaliseContent(content);
                    else
                        db.ProjectDocSections.Add(new ProjectDocSection
                        {
                            ProjectId = projectId,
                            SectionId = sectionId,
                            Content = NormaliseContent(content),
                        });
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                anyFailure = true;
                logger.LogWarning(ex, "Failed to pull doc '{DocKey}' from repo for project {ProjectId}.", docKey, projectId);
            }
        }

        if (!anyFailure)
            await audit.WriteAsync(new AuditWriteRequest("Project", projectId, "project:docs-pulled-from-repo", AuditOutcome.Granted)
            {
                ProjectId = projectId,
            }, ct);

        return !anyFailure;
    }

    /// <summary>
    /// Parses an <see cref="AssembleDocMarkdown"/>-formatted string back into a
    /// <c>sectionId → content</c> dictionary using the supplied template sections to
    /// map <c>## Label</c> headers to IDs.
    /// </summary>
    internal static Dictionary<Guid, string> ParseDocMarkdown(
        string markdown, IEnumerable<Entities.DocTemplateSection> templateSections)
    {
        var labelToSection = templateSections
            .ToDictionary(s => s.Label, StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<Guid, string>();
        Entities.DocTemplateSection? current = null;
        var contentLines = new System.Collections.Generic.List<string>();

        void Flush()
        {
            if (current is null) return;
            var content = string.Join('\n', contentLines).Trim();
            if (content.Length > 0) result[current.Id] = content;
            contentLines.Clear();
            current = null;
        }

        foreach (var line in markdown.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                labelToSection.TryGetValue(line[3..].Trim(), out current);
            }
            else if (current is not null)
            {
                contentLines.Add(line.TrimEnd('\r'));
            }
        }
        Flush();

        return result;
    }

    private async Task<Guid> GetVersionIdAsync(Guid projectId, CancellationToken ct)
    {
        var versionId = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.DocTemplateVersionId)
            .FirstOrDefaultAsync(ct);

        if (versionId is null || versionId == Guid.Empty)
            throw new NotFoundException("Project not found.");

        return versionId.Value;
    }

    private static string? NormaliseContent(string content) =>
        string.IsNullOrWhiteSpace(content) ? null : content.Trim();

    internal static bool IsFilled(string? content) => !string.IsNullOrWhiteSpace(content);

    /// <summary>
    /// Renders a doc as Markdown: a top-level heading followed by each section as an H2 block.
    /// Sections with no content emit the heading only. Caller supplies sections in display order.
    /// </summary>
    internal static string AssembleDocMarkdown(string docLabel, IEnumerable<(string Label, string? Content)> sections)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("# ").AppendLine(docLabel);

        foreach (var (label, content) in sections)
        {
            sb.AppendLine();
            sb.Append("## ").AppendLine(label);
            if (IsFilled(content))
            {
                sb.AppendLine();
                sb.AppendLine(content!.Trim());
            }
        }

        return sb.ToString();
    }
}
