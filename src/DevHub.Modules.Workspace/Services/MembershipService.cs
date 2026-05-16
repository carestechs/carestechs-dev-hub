using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Contracts.Identity;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Workspace.Services;

public interface IMembershipService
{
    Task<IReadOnlyList<ProjectMembershipDto>> ListAsync(Guid projectId, Guid callerMemberId, CancellationToken ct);
    Task<ProjectMembershipDto> AddAsync(Guid projectId, AddMembershipRequest req, Guid actingMemberId, CancellationToken ct);
    Task<ProjectMembershipDto> UpdateAsync(Guid projectId, Guid membershipId, UpdateMembershipRequest req, Guid actingMemberId, CancellationToken ct);
    Task RemoveAsync(Guid projectId, Guid membershipId, Guid actingMemberId, CancellationToken ct);
}

internal sealed class MembershipService(
    WorkspaceDbContext db,
    IProjectAuthorizationService authz,
    IAuditWriter audit) : IMembershipService
{
    public async Task<IReadOnlyList<ProjectMembershipDto>> ListAsync(Guid projectId, Guid callerMemberId, CancellationToken ct)
    {
        await authz.EnsureAuthorizedAsync(callerMemberId, projectId, "project:membership:list", requiredRoleKey: null, ct);

        var memberships = await (
            from pm in db.ProjectMemberships.AsNoTracking()
            where pm.ProjectId == projectId
            join m in db.Members.AsNoTracking() on pm.MemberId equals m.Id
            select new { pm.Id, pm.CreatedAt, m.DisplayName, m.Email, MemberId = m.Id }
        ).ToListAsync(ct);

        var ids = memberships.Select(x => x.Id).ToList();
        var roleMap = await (
            from ra in db.RoleAssignments.AsNoTracking()
            where ids.Contains(ra.ProjectMembershipId)
            join r in db.Roles.AsNoTracking() on ra.RoleId equals r.Id
            select new { ra.ProjectMembershipId, r.Key }
        ).ToListAsync(ct);
        var rolesByMembership = roleMap
            .GroupBy(x => x.ProjectMembershipId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Key).Distinct().ToList());

        return memberships
            .Select(m => new ProjectMembershipDto(
                m.Id,
                new MemberRefDto(m.MemberId, m.DisplayName, m.Email),
                rolesByMembership.TryGetValue(m.Id, out var rl) ? rl : Array.Empty<string>(),
                m.CreatedAt))
            .ToList();
    }

    public async Task<ProjectMembershipDto> AddAsync(Guid projectId, AddMembershipRequest req, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "project:membership:add", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project not found.");
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == req.MemberId, ct)
            ?? throw new NotFoundException("Member not found.");
        if (member.Status != MemberStatus.Active)
            throw new ConflictException("Cannot add a non-active member to a project.");

        if (await db.ProjectMemberships.AnyAsync(pm => pm.ProjectId == projectId && pm.MemberId == req.MemberId, ct))
            throw new ConflictException("Member already belongs to this project.");

        var requestedRoles = await db.Roles
            .Where(r => req.RoleKeys.Contains(r.Key))
            .ToListAsync(ct);
        var missing = req.RoleKeys.Except(requestedRoles.Select(r => r.Key)).ToList();
        if (missing.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["roleKeys"] = [$"Unknown role keys: {string.Join(", ", missing)}"],
            });

        var membership = new ProjectMembership
        {
            ProjectId = projectId,
            MemberId = req.MemberId,
            CreatedByMemberId = actingMemberId,
        };
        db.ProjectMemberships.Add(membership);
        foreach (var role in requestedRoles)
        {
            db.RoleAssignments.Add(new RoleAssignment
            {
                ProjectMembershipId = membership.Id,
                RoleId = role.Id,
                CreatedByMemberId = actingMemberId,
            });
        }

        await audit.WriteAsync(new AuditWriteRequest("ProjectMembership", membership.Id, "project:membership:add", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            ProjectId = projectId,
            Details = new Dictionary<string, object?>
            {
                ["memberId"] = req.MemberId,
                ["roleKeys"] = req.RoleKeys,
            },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ProjectMembershipDto(
            membership.Id,
            new MemberRefDto(member.Id, member.DisplayName, member.Email),
            req.RoleKeys.ToList(),
            membership.CreatedAt);
    }

    public async Task<ProjectMembershipDto> UpdateAsync(Guid projectId, Guid membershipId, UpdateMembershipRequest req, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "project:membership:update", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var membership = await db.ProjectMemberships
            .FirstOrDefaultAsync(pm => pm.Id == membershipId && pm.ProjectId == projectId, ct)
            ?? throw new NotFoundException("Membership not found.");

        var requestedRoles = await db.Roles
            .Where(r => req.RoleKeys.Contains(r.Key))
            .ToListAsync(ct);
        var missing = req.RoleKeys.Except(requestedRoles.Select(r => r.Key)).ToList();
        if (missing.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["roleKeys"] = [$"Unknown role keys: {string.Join(", ", missing)}"],
            });

        // Replace strategy: soft-delete existing assignments, add the new set.
        var now = DateTimeOffset.UtcNow;
        await db.RoleAssignments
            .Where(ra => ra.ProjectMembershipId == membershipId)
            .ExecuteUpdateAsync(s => s.SetProperty(ra => ra.DeletedAt, now), ct);
        foreach (var role in requestedRoles)
        {
            db.RoleAssignments.Add(new RoleAssignment
            {
                ProjectMembershipId = membership.Id,
                RoleId = role.Id,
                CreatedByMemberId = actingMemberId,
            });
        }

        await audit.WriteAsync(new AuditWriteRequest("ProjectMembership", membership.Id, "project:membership:update", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            ProjectId = projectId,
            Details = new Dictionary<string, object?> { ["roleKeys"] = req.RoleKeys },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var member = await db.Members.AsNoTracking().FirstAsync(m => m.Id == membership.MemberId, ct);
        return new ProjectMembershipDto(
            membership.Id,
            new MemberRefDto(member.Id, member.DisplayName, member.Email),
            req.RoleKeys.ToList(),
            membership.CreatedAt);
    }

    public async Task RemoveAsync(Guid projectId, Guid membershipId, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "project:membership:remove", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var membership = await db.ProjectMemberships
            .FirstOrDefaultAsync(pm => pm.Id == membershipId && pm.ProjectId == projectId, ct)
            ?? throw new NotFoundException("Membership not found.");

        var now = DateTimeOffset.UtcNow;
        await db.RoleAssignments
            .Where(ra => ra.ProjectMembershipId == membershipId)
            .ExecuteUpdateAsync(s => s.SetProperty(ra => ra.DeletedAt, now), ct);
        membership.DeletedAt = now;

        await audit.WriteAsync(new AuditWriteRequest("ProjectMembership", membership.Id, "project:membership:remove", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            ProjectId = projectId,
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
