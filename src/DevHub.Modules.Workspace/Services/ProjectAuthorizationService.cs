using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Workspace.Services;

internal sealed class ProjectAuthorizationService(
    WorkspaceDbContext db,
    IProjectMembershipQuery memberships,
    IAuditWriter audit) : IProjectAuthorizationService
{
    public async Task<AuthorizationOutcome> AuthorizeAsync(
        Guid memberId,
        Guid projectId,
        string action,
        string? requiredRoleKey = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Operator? Workspace-wide grant.
        if (await memberships.IsOperatorAsync(memberId, cancellationToken))
        {
            await audit.WriteAsync(new AuditWriteRequest(
                TargetType: "Project",
                TargetId: projectId,
                Action: action,
                Outcome: AuditOutcome.Granted)
            {
                ActingMemberId = memberId,
                ProjectId = projectId,
                Reason = "operator",
                Details = new Dictionary<string, object?>
                {
                    ["isOperator"] = true,
                    ["requiredRoleKey"] = requiredRoleKey,
                },
            }, cancellationToken);
            return AuthorizationOutcome.Allow();
        }

        // 2. ProjectMembership check.
        var membership = await db.ProjectMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(pm => pm.MemberId == memberId && pm.ProjectId == projectId, cancellationToken);
        if (membership is null)
        {
            return await DenyAsync(memberId, projectId, action, requiredRoleKey,
                "member is not on this project", cancellationToken);
        }

        // 3. Any-role read? requiredRoleKey null means "must be a member, no role check".
        if (requiredRoleKey is null)
        {
            await audit.WriteAsync(new AuditWriteRequest(
                TargetType: "Project",
                TargetId: projectId,
                Action: action,
                Outcome: AuditOutcome.Granted)
            {
                ActingMemberId = memberId,
                ProjectId = projectId,
                Details = new Dictionary<string, object?> { ["membership"] = "any-role" },
            }, cancellationToken);
            return AuthorizationOutcome.Allow();
        }

        // 4. Role-on-membership match.
        var hasRole = await (
            from ra in db.RoleAssignments
            where ra.ProjectMembershipId == membership.Id
            join r in db.Roles on ra.RoleId equals r.Id
            where r.Key == requiredRoleKey
            select ra.Id).AnyAsync(cancellationToken);
        if (!hasRole)
        {
            return await DenyAsync(memberId, projectId, action, requiredRoleKey,
                $"member lacks role '{requiredRoleKey}'", cancellationToken);
        }

        await audit.WriteAsync(new AuditWriteRequest(
            TargetType: "Project",
            TargetId: projectId,
            Action: action,
            Outcome: AuditOutcome.Granted)
        {
            ActingMemberId = memberId,
            ProjectId = projectId,
            Details = new Dictionary<string, object?> { ["requiredRoleKey"] = requiredRoleKey },
        }, cancellationToken);
        return AuthorizationOutcome.Allow();
    }

    public async Task EnsureAuthorizedAsync(
        Guid memberId,
        Guid projectId,
        string action,
        string? requiredRoleKey = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await AuthorizeAsync(memberId, projectId, action, requiredRoleKey, cancellationToken);
        if (!outcome.Granted)
        {
            throw new ForbiddenException(outcome.DeniedReason ?? "Forbidden.");
        }
    }

    public async Task EnsureOperatorAsync(Guid memberId, string action, CancellationToken cancellationToken = default)
    {
        if (await memberships.IsOperatorAsync(memberId, cancellationToken))
        {
            await audit.WriteAsync(new AuditWriteRequest(
                TargetType: "Workspace",
                TargetId: null,
                Action: action,
                Outcome: AuditOutcome.Granted)
            {
                ActingMemberId = memberId,
                Reason = "operator",
            }, cancellationToken);
            return;
        }
        await audit.WriteAsync(new AuditWriteRequest(
            TargetType: "Workspace",
            TargetId: null,
            Action: action,
            Outcome: AuditOutcome.Denied)
        {
            ActingMemberId = memberId,
            Reason = "not an operator",
        }, cancellationToken);
        throw new ForbiddenException("Operator role required.");
    }

    private async Task<AuthorizationOutcome> DenyAsync(
        Guid memberId,
        Guid projectId,
        string action,
        string? requiredRoleKey,
        string reason,
        CancellationToken cancellationToken)
    {
        await audit.WriteAsync(new AuditWriteRequest(
            TargetType: "Project",
            TargetId: projectId,
            Action: action,
            Outcome: AuditOutcome.Denied)
        {
            ActingMemberId = memberId,
            ProjectId = projectId,
            Reason = reason,
            Details = new Dictionary<string, object?> { ["requiredRoleKey"] = requiredRoleKey },
        }, cancellationToken);
        return AuthorizationOutcome.Deny(reason);
    }
}
