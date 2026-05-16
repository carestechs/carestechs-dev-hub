using DevHub.Contracts.Executors;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.ExecutorRegistry.Services;

/// <summary>
/// Single source of <c>Environment.GetEnvironmentVariable</c> reads for executor credentials. The
/// resolved value is intentionally not cached and must be used transiently by the caller.
/// </summary>
internal sealed class ExecutorCredentialResolver(ExecutorRegistryDbContext db) : IExecutorCredentialResolver
{
    public async Task<string?> ResolveAsync(Guid executorId, CancellationToken cancellationToken = default)
    {
        var refName = await db.Executors
            .AsNoTracking()
            .Where(e => e.Id == executorId && e.DeletedAt == null)
            .Select(e => e.CredentialsRef)
            .FirstOrDefaultAsync(cancellationToken);
        return refName is null ? null : Environment.GetEnvironmentVariable(refName);
    }
}
