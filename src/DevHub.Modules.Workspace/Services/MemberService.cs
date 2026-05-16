using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Contracts.Identity;
using DevHub.Contracts.Pagination;
using DevHub.Modules.Workspace.DTOs;
using DevHub.Modules.Workspace.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Workspace.Services;

public interface IMemberService
{
    Task<PagedEnvelopeDto<MemberDto>> ListAsync(PageRequest page, string? q, CancellationToken ct);
    Task<MemberDto> GetAsync(Guid id, CancellationToken ct);
    Task<MemberDto> InviteAsync(InviteMemberRequest req, Guid actingMemberId, CancellationToken ct);
    Task<MemberDto> UpdateAsync(Guid id, UpdateMemberRequest req, Guid actingMemberId, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid actingMemberId, CancellationToken ct);
}

internal sealed class MemberService(
    WorkspaceDbContext db,
    IProjectAuthorizationService authz,
    IAuditWriter audit) : IMemberService
{
    private const string OperatorRoleKey = "operator";

    public async Task<PagedEnvelopeDto<MemberDto>> ListAsync(PageRequest page, string? q, CancellationToken ct)
    {
        IQueryable<Member> query = db.Members.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            query = query.Where(m => EF.Functions.ILike(m.DisplayName, like) || EF.Functions.ILike(m.Email, like));
        }

        var totalCount = await query.CountAsync(ct);
        query = (page.SortBy?.ToLowerInvariant(), page.SortDir) switch
        {
            ("displayname", "asc") => query.OrderBy(m => m.DisplayName),
            ("displayname", _)     => query.OrderByDescending(m => m.DisplayName),
            ("email", "asc")       => query.OrderBy(m => m.Email),
            ("email", _)           => query.OrderByDescending(m => m.Email),
            (_, "asc")             => query.OrderBy(m => m.CreatedAt),
            _                      => query.OrderByDescending(m => m.CreatedAt),
        };

        var rows = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(m => new MemberDto(m.Id, m.DisplayName, m.Email, m.Status, m.CreatedAt))
            .ToListAsync(ct);

        return new PagedEnvelopeDto<MemberDto>(rows, new PageMeta(totalCount, page.Page, page.PageSize, page.SortBy, page.SortDir));
    }

    public async Task<MemberDto> GetAsync(Guid id, CancellationToken ct)
    {
        var member = await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("Member not found.");
        return new MemberDto(member.Id, member.DisplayName, member.Email, member.Status, member.CreatedAt);
    }

    public async Task<MemberDto> InviteAsync(InviteMemberRequest req, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "member:invite", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await db.Members.AnyAsync(m => m.Email == req.Email, ct))
            throw new ConflictException($"A member with email '{req.Email}' already exists.");

        var member = new Member
        {
            DisplayName = req.DisplayName,
            Email = req.Email,
            Status = MemberStatus.Invited,
        };
        db.Members.Add(member);

        await audit.WriteAsync(new AuditWriteRequest("Member", member.Id, "member:invite", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            Details = new Dictionary<string, object?> { ["email"] = req.Email },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new MemberDto(member.Id, member.DisplayName, member.Email, member.Status, member.CreatedAt);
    }

    public async Task<MemberDto> UpdateAsync(Guid id, UpdateMemberRequest req, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "member:update", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("Member not found.");

        if (req.DisplayName is not null) member.DisplayName = req.DisplayName;

        if (req.Status is { } newStatus && newStatus != member.Status)
        {
            // Suspending an operator must leave at least one operator member behind.
            if (newStatus == MemberStatus.Suspended)
            {
                await EnsureNotLastOperatorAsync(member.Id, ct, "Cannot suspend the last operator.");
            }
            member.Status = newStatus;
        }

        await audit.WriteAsync(new AuditWriteRequest("Member", member.Id, "member:update", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            Details = new Dictionary<string, object?> { ["status"] = member.Status.ToString() },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new MemberDto(member.Id, member.DisplayName, member.Email, member.Status, member.CreatedAt);
    }

    public async Task DeleteAsync(Guid id, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "member:delete", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("Member not found.");

        await EnsureNotLastOperatorAsync(id, ct, "Cannot delete the last operator.");

        member.DeletedAt = DateTimeOffset.UtcNow;

        // Soft-cascade: this member's workspace + project role grants go away with them.
        await db.WorkspaceRoleAssignments
            .Where(w => w.MemberId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.DeletedAt, DateTimeOffset.UtcNow), ct);

        var membershipIds = await db.ProjectMemberships
            .Where(pm => pm.MemberId == id)
            .Select(pm => pm.Id)
            .ToListAsync(ct);
        if (membershipIds.Count > 0)
        {
            await db.RoleAssignments
                .Where(ra => membershipIds.Contains(ra.ProjectMembershipId))
                .ExecuteUpdateAsync(s => s.SetProperty(ra => ra.DeletedAt, DateTimeOffset.UtcNow), ct);
            await db.ProjectMemberships
                .Where(pm => pm.MemberId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(pm => pm.DeletedAt, DateTimeOffset.UtcNow), ct);
        }

        await audit.WriteAsync(new AuditWriteRequest("Member", member.Id, "member:delete", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task EnsureNotLastOperatorAsync(Guid memberId, CancellationToken ct, string failMessage)
    {
        var isOperator = await (
            from w in db.WorkspaceRoleAssignments
            join r in db.Roles on w.RoleId equals r.Id
            where w.MemberId == memberId && r.Key == OperatorRoleKey
            select w.Id).AnyAsync(ct);
        if (!isOperator) return;

        var otherActiveOperators = await (
            from w in db.WorkspaceRoleAssignments
            join r in db.Roles on w.RoleId equals r.Id
            join m in db.Members on w.MemberId equals m.Id
            where w.MemberId != memberId
               && r.Key == OperatorRoleKey
               && m.Status == MemberStatus.Active
            select w.Id).CountAsync(ct);
        if (otherActiveOperators == 0)
        {
            throw new ConflictException(failMessage);
        }
    }
}
