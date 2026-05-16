using DevHub.Contracts.Workspace;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Workspace.Services;

internal sealed class RoleLookup(WorkspaceDbContext db) : IRoleLookup
{
    public async Task<bool> ExistsAsync(string roleKey, CancellationToken cancellationToken = default) =>
        await db.Roles.AsNoTracking().AnyAsync(r => r.Key == roleKey, cancellationToken);

    public async Task<IReadOnlyList<string>> GetMissingAsync(IEnumerable<string> roleKeys, CancellationToken cancellationToken = default)
    {
        var asked = roleKeys.Distinct().ToList();
        if (asked.Count == 0) return Array.Empty<string>();
        var existing = await db.Roles.AsNoTracking()
            .Where(r => asked.Contains(r.Key))
            .Select(r => r.Key)
            .ToListAsync(cancellationToken);
        return asked.Except(existing).ToList();
    }
}
