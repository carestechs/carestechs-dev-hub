using DevHub.Contracts.Identity;
using DevHub.Modules.Workspace.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Workspace.Services;

internal sealed class MemberLookup(WorkspaceDbContext db) : IMemberLookup
{
    public async Task<MemberLookupResult?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Email == email, cancellationToken);
        return member is null ? null : Map(member);
    }

    public async Task<MemberLookupResult?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        return member is null ? null : Map(member);
    }

    private static MemberLookupResult Map(Member m) =>
        new(m.Id, m.DisplayName, m.Email, m.Status.ToString());
}
