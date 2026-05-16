using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Contracts.Pagination;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Workspace.Services;

public interface ITeamService
{
    Task<PagedEnvelopeDto<TeamDto>> ListAsync(PageRequest page, CancellationToken ct);
    Task<TeamDto> GetAsync(Guid id, CancellationToken ct);
    Task<TeamDto> CreateAsync(CreateTeamRequest req, Guid actingMemberId, CancellationToken ct);
    Task<TeamDto> UpdateAsync(Guid id, UpdateTeamRequest req, Guid actingMemberId, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid actingMemberId, CancellationToken ct);
}

internal sealed class TeamService(
    WorkspaceDbContext db,
    IProjectAuthorizationService authz,
    IAuditWriter audit) : ITeamService
{
    public async Task<PagedEnvelopeDto<TeamDto>> ListAsync(PageRequest page, CancellationToken ct)
    {
        IQueryable<Team> query = db.Teams.AsNoTracking();
        var totalCount = await query.CountAsync(ct);

        query = (page.SortBy?.ToLowerInvariant(), page.SortDir) switch
        {
            ("name", "asc") => query.OrderBy(t => t.Name),
            ("name", _)     => query.OrderByDescending(t => t.Name),
            (_, "asc")      => query.OrderBy(t => t.CreatedAt),
            _               => query.OrderByDescending(t => t.CreatedAt),
        };

        var rows = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(t => new
            {
                t.Id, t.Name, t.Description, t.CreatedAt,
                ProjectCount = db.Projects.Count(p => p.OwningTeamId == t.Id),
            })
            .ToListAsync(ct);

        return new PagedEnvelopeDto<TeamDto>(
            rows.Select(r => new TeamDto(r.Id, r.Name, r.Description, r.ProjectCount, r.CreatedAt)).ToList(),
            new PageMeta(totalCount, page.Page, page.PageSize, page.SortBy, page.SortDir));
    }

    public async Task<TeamDto> GetAsync(Guid id, CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Team not found.");
        var projectCount = await db.Projects.CountAsync(p => p.OwningTeamId == id, ct);
        return new TeamDto(team.Id, team.Name, team.Description, projectCount, team.CreatedAt);
    }

    public async Task<TeamDto> CreateAsync(CreateTeamRequest req, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "team:create", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await db.Teams.AnyAsync(t => t.Name == req.Name, ct))
            throw new ConflictException($"Team '{req.Name}' already exists.");

        var team = new Team { Name = req.Name, Description = req.Description };
        db.Teams.Add(team);

        await audit.WriteAsync(new AuditWriteRequest("Team", team.Id, "team:create", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            Details = new Dictionary<string, object?> { ["name"] = req.Name },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new TeamDto(team.Id, team.Name, team.Description, ProjectCount: 0, team.CreatedAt);
    }

    public async Task<TeamDto> UpdateAsync(Guid id, UpdateTeamRequest req, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "team:update", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Team not found.");

        if (req.Name is not null && req.Name != team.Name)
        {
            if (await db.Teams.AnyAsync(t => t.Name == req.Name && t.Id != id, ct))
                throw new ConflictException($"Team '{req.Name}' already exists.");
            team.Name = req.Name;
        }
        if (req.Description is not null) team.Description = req.Description;

        await audit.WriteAsync(new AuditWriteRequest("Team", team.Id, "team:update", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var projectCount = await db.Projects.CountAsync(p => p.OwningTeamId == team.Id, ct);
        return new TeamDto(team.Id, team.Name, team.Description, projectCount, team.CreatedAt);
    }

    public async Task DeleteAsync(Guid id, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "team:delete", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Team not found.");

        if (await db.Projects.AnyAsync(p => p.OwningTeamId == id, ct))
            throw new ConflictException("Cannot delete a team that owns projects.");

        team.DeletedAt = DateTimeOffset.UtcNow;

        await audit.WriteAsync(new AuditWriteRequest("Team", team.Id, "team:delete", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
