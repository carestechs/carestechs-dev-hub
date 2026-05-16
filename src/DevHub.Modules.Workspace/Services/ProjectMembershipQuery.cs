using DevHub.Contracts.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Workspace.Services;

internal sealed class ProjectMembershipQuery(WorkspaceDbContext db) : IProjectMembershipQuery
{
    public const string OperatorRoleKey = "operator";

    public async Task<IReadOnlyList<MembershipDescriptor>> GetMembershipsAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        // Memberships × role assignments × roles × projects in one query (filtered by global query filters
        // for soft delete) — Postgres + EF translate this to a single SQL statement.
        var rows = await (
            from pm in db.ProjectMemberships
            where pm.MemberId == memberId
            join p in db.Projects on pm.ProjectId equals p.Id
            join ra in db.RoleAssignments on pm.Id equals ra.ProjectMembershipId
            join r in db.Roles on ra.RoleId equals r.Id
            select new { p.Id, p.Slug, RoleKey = r.Key })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => new { r.Id, r.Slug })
            .Select(g => new MembershipDescriptor(
                ProjectId: g.Key.Id,
                ProjectSlug: g.Key.Slug,
                Roles: g.Select(x => x.RoleKey).Distinct().ToList()))
            .ToList();
    }

    public async Task<bool> IsOperatorAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        return await (
            from w in db.WorkspaceRoleAssignments
            where w.MemberId == memberId
            join r in db.Roles on w.RoleId equals r.Id
            where r.Key == OperatorRoleKey
            select w.Id)
            .AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetMembersWithRoleAsync(
        Guid projectId, string roleKey, CancellationToken cancellationToken = default)
    {
        return await (
            from pm in db.ProjectMemberships
            where pm.ProjectId == projectId
            join ra in db.RoleAssignments on pm.Id equals ra.ProjectMembershipId
            join r in db.Roles on ra.RoleId equals r.Id
            where r.Key == roleKey
            select pm.MemberId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetWorkspaceOperatorsAsync(CancellationToken cancellationToken = default)
    {
        return await (
            from w in db.WorkspaceRoleAssignments
            join r in db.Roles on w.RoleId equals r.Id
            where r.Key == OperatorRoleKey
            select w.MemberId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
