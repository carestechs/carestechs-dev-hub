using System.Text.Json;
using DevHub.Contracts.Executors;
using DevHub.Contracts.Workspace;
using DevHub.Modules.ExecutorRegistry.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.ExecutorRegistry.Services;

internal sealed class ExecutorRouter(
    ExecutorRegistryDbContext db,
    IProjectLookup projects) : IExecutorRouter
{
    public async Task<bool> IsProjectTypeBoundAsync(string projectType, CancellationToken cancellationToken = default) =>
        await db.Bindings
            .AsNoTracking()
            .AnyAsync(b => b.ProjectType == projectType && b.DeletedAt == null, cancellationToken);

    public async Task<ExecutorRegistrationDescriptor?> ResolveAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var projectType = await projects.GetProjectTypeAsync(projectId, cancellationToken);
        if (projectType is null) return null;

        var executorId = await db.Bindings
            .AsNoTracking()
            .Where(b => b.ProjectType == projectType && b.DeletedAt == null)
            .Select(b => (Guid?)b.ExecutorId)
            .FirstOrDefaultAsync(cancellationToken);
        if (executorId is null) return null;

        var exec = await db.Executors
            .AsNoTracking()
            .Include(e => e.CheckpointContracts)
            .FirstOrDefaultAsync(e => e.Id == executorId && e.DeletedAt == null, cancellationToken);
        return exec is null ? null : Map(exec);
    }

    public async Task<CheckpointContractDescriptor?> GetCheckpointContractAsync(
        Guid executorId, string checkpointKey, CancellationToken cancellationToken = default)
    {
        var c = await db.CheckpointContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ExecutorId == executorId && c.CheckpointKey == checkpointKey, cancellationToken);
        return c is null ? null : Map(c);
    }

    internal static ExecutorRegistrationDescriptor Map(ExecutorRegistration e) =>
        new(e.Id, e.Key, e.DisplayName, e.BaseUrl, e.Status,
            e.CheckpointContracts.Select(Map).ToList());

    internal static CheckpointContractDescriptor Map(CheckpointContract c) =>
        new(c.CheckpointKey, c.DisplayName, c.RequiredRoleKey, ParseOutcomes(c.AllowedOutcomesJson));

    internal static IReadOnlyList<string> ParseOutcomes(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }
}
