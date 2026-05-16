namespace DevHub.Contracts.Authorization;

/// <summary>
/// The end-to-end authorization gate. Every façade endpoint that wraps a project-scoped or
/// workspace-level action calls into here BEFORE forwarding to the executor or mutating data.
/// The service writes the audit row (Granted or Denied) on the caller's behalf so individual
/// endpoints can't forget to audit.
/// </summary>
public interface IProjectAuthorizationService
{
    /// Resolves <c>(memberId, projectId, action, requiredRoleKey?)</c>. Operators are granted
    /// workspace-wide. Writes an audit entry; returns the outcome.
    Task<AuthorizationOutcome> AuthorizeAsync(
        Guid memberId,
        Guid projectId,
        string action,
        string? requiredRoleKey = null,
        CancellationToken cancellationToken = default);

    /// Same as <see cref="AuthorizeAsync"/> but throws <c>ForbiddenException</c> on Denied —
    /// the conventional shape for controllers.
    Task EnsureAuthorizedAsync(
        Guid memberId,
        Guid projectId,
        string action,
        string? requiredRoleKey = null,
        CancellationToken cancellationToken = default);

    /// Workspace-level operator check (no project context). Used by endpoints that create
    /// teams, invite members, or otherwise manage workspace primitives. Audits + throws on
    /// deny.
    Task EnsureOperatorAsync(Guid memberId, string action, CancellationToken cancellationToken = default);
}
