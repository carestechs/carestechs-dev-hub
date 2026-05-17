using System.Text.Json;
using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Contracts.Executors;
using DevHub.Contracts.Pagination;
using DevHub.Contracts.Workspace;
using DevHub.Modules.ExecutorRegistry.DTOs;
using DevHub.Modules.ExecutorRegistry.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.ExecutorRegistry.Services;

public interface IExecutorRegistrationService
{
    Task<PagedEnvelopeDto<ExecutorDto>> ListAsync(PageRequest page, ExecutorStatus? status, CancellationToken ct);
    Task<ExecutorDto> GetAsync(Guid id, CancellationToken ct);
    Task<ExecutorDto> CreateAsync(CreateExecutorRequest req, Guid actingMemberId, CancellationToken ct);
    Task<ExecutorDto> UpdateAsync(Guid id, UpdateExecutorRequest req, Guid actingMemberId, CancellationToken ct);
    Task<ExecutorDto> ReplaceContractsAsync(Guid id, ReplaceContractsRequest req, Guid actingMemberId, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid actingMemberId, CancellationToken ct);
}

internal sealed class ExecutorRegistrationService(
    ExecutorRegistryDbContext db,
    IProjectAuthorizationService authz,
    IRoleLookup roles,
    IAuditWriter audit) : IExecutorRegistrationService
{
    public async Task<PagedEnvelopeDto<ExecutorDto>> ListAsync(PageRequest page, ExecutorStatus? status, CancellationToken ct)
    {
        IQueryable<ExecutorRegistration> q = db.Executors.AsNoTracking()
            .Include(e => e.CheckpointContracts)
            .Where(e => e.DeletedAt == null);
        if (status is { } s) q = q.Where(e => e.Status == s);

        var totalCount = await q.CountAsync(ct);

        q = (page.SortBy?.ToLowerInvariant(), page.SortDir) switch
        {
            ("key", "asc") => q.OrderBy(e => e.Key),
            ("key", _)     => q.OrderByDescending(e => e.Key),
            (_, "asc")     => q.OrderBy(e => e.CreatedAt),
            _              => q.OrderByDescending(e => e.CreatedAt),
        };

        var rows = await q
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return new PagedEnvelopeDto<ExecutorDto>(
            rows.Select(MapDto).ToList(),
            new PageMeta(totalCount, page.Page, page.PageSize, page.SortBy, page.SortDir));
    }

    public async Task<ExecutorDto> GetAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Executors.AsNoTracking()
            .Include(x => x.CheckpointContracts)
            .FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
            ?? throw new NotFoundException("Executor not found.");
        return MapDto(e);
    }

    public async Task<ExecutorDto> CreateAsync(CreateExecutorRequest req, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "executor:create", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await db.Executors.AnyAsync(e => e.Key == req.Key && e.DeletedAt == null, ct))
            throw new ConflictException($"An executor with key '{req.Key}' already exists.");

        var requiredKeys = req.CheckpointContracts.Select(c => c.RequiredRoleKey).ToList();
        if (requiredKeys.Count > 0)
        {
            var missing = await roles.GetMissingAsync(requiredKeys, ct);
            if (missing.Count > 0)
                throw new ValidationException(
                    new Dictionary<string, string[]>
                    {
                        ["checkpointContracts.requiredRoleKey"] = new[] { $"Unknown role keys: {string.Join(", ", missing)}" },
                    });
        }

        var entry = new ExecutorRegistration
        {
            Key = req.Key,
            DisplayName = req.DisplayName,
            BaseUrl = req.BaseUrl,
            CredentialsRef = req.CredentialsRef,
            Status = ExecutorStatus.Active,
        };
        foreach (var c in req.CheckpointContracts)
        {
            entry.CheckpointContracts.Add(new CheckpointContract
            {
                ExecutorId = entry.Id,
                CheckpointKey = c.CheckpointKey,
                DisplayName = c.DisplayName,
                RequiredRoleKey = c.RequiredRoleKey,
                AllowedOutcomesJson = JsonSerializer.Serialize(c.AllowedOutcomes),
                PerTask = c.PerTask,
            });
        }
        db.Executors.Add(entry);

        await audit.WriteAsync(new AuditWriteRequest("ExecutorRegistration", entry.Id, "executor:create", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            Details = new Dictionary<string, object?>
            {
                ["key"] = req.Key,
                ["contractsCount"] = req.CheckpointContracts.Count,
            },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await GetAsync(entry.Id, ct);
    }

    public async Task<ExecutorDto> UpdateAsync(Guid id, UpdateExecutorRequest req, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "executor:update", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var e = await db.Executors.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
            ?? throw new NotFoundException("Executor not found.");

        if (req.DisplayName is not null) e.DisplayName = req.DisplayName;
        if (req.BaseUrl is not null) e.BaseUrl = req.BaseUrl;
        if (req.CredentialsRef is not null) e.CredentialsRef = req.CredentialsRef;
        if (req.Status is { } status) e.Status = status;

        await audit.WriteAsync(new AuditWriteRequest("ExecutorRegistration", e.Id, "executor:update", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            Details = new Dictionary<string, object?>
            {
                ["status"] = e.Status.ToString(),
            },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await GetAsync(e.Id, ct);
    }

    public async Task<ExecutorDto> ReplaceContractsAsync(Guid id, ReplaceContractsRequest req, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "executor:replace-contracts", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (!await db.Executors.AnyAsync(x => x.Id == id && x.DeletedAt == null, ct))
            throw new NotFoundException("Executor not found.");

        var requiredKeys = req.CheckpointContracts.Select(c => c.RequiredRoleKey).ToList();
        var missing = await roles.GetMissingAsync(requiredKeys, ct);
        if (missing.Count > 0)
            throw new ValidationException(
                new Dictionary<string, string[]>
                {
                    ["checkpointContracts.requiredRoleKey"] = new[] { $"Unknown role keys: {string.Join(", ", missing)}" },
                });

        await db.CheckpointContracts.Where(c => c.ExecutorId == id).ExecuteDeleteAsync(ct);
        foreach (var c in req.CheckpointContracts)
        {
            db.CheckpointContracts.Add(new CheckpointContract
            {
                ExecutorId = id,
                CheckpointKey = c.CheckpointKey,
                DisplayName = c.DisplayName,
                RequiredRoleKey = c.RequiredRoleKey,
                AllowedOutcomesJson = JsonSerializer.Serialize(c.AllowedOutcomes),
                PerTask = c.PerTask,
            });
        }

        await audit.WriteAsync(new AuditWriteRequest("ExecutorRegistration", id, "executor:replace-contracts", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            Details = new Dictionary<string, object?> { ["contractsCount"] = req.CheckpointContracts.Count },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "executor:delete", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var e = await db.Executors.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
            ?? throw new NotFoundException("Executor not found.");

        if (await db.Bindings.AnyAsync(b => b.ExecutorId == id && b.DeletedAt == null, ct))
            throw new ConflictException("Cannot delete an executor with active bindings.");

        e.DeletedAt = DateTimeOffset.UtcNow;

        await audit.WriteAsync(new AuditWriteRequest("ExecutorRegistration", e.Id, "executor:delete", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static ExecutorDto MapDto(ExecutorRegistration e) =>
        new(e.Id, e.Key, e.DisplayName, e.BaseUrl, e.CredentialsRef, e.Status,
            e.CheckpointContracts.Select(c => new CheckpointContractDto(
                c.CheckpointKey, c.DisplayName, c.RequiredRoleKey, ExecutorRouter.ParseOutcomes(c.AllowedOutcomesJson), c.PerTask)).ToList(),
            e.CreatedAt);
}
