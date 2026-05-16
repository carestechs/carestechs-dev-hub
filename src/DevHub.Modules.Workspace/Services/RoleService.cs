using DevHub.Modules.Workspace.DTOs;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Workspace.Services;

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct);
}

internal sealed class RoleService(WorkspaceDbContext db) : IRoleService
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct)
    {
        return await db.Roles.AsNoTracking()
            .OrderBy(r => r.Key)
            .Select(r => new RoleDto(r.Id, r.Key, r.Name, r.Description, r.IsSystem))
            .ToListAsync(ct);
    }
}
