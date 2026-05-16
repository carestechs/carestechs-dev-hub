using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Audit;
using DevHub.Contracts.Authorization;
using DevHub.Contracts.Pagination;
using DevHub.Modules.ExecutorRegistry.DTOs;
using DevHub.Modules.ExecutorRegistry.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.ExecutorRegistry.Services;

public interface IExecutorBindingService
{
    Task<PagedEnvelopeDto<ExecutorBindingDto>> ListAsync(PageRequest page, CancellationToken ct);
    Task<ExecutorBindingDto> CreateAsync(CreateBindingRequest req, Guid actingMemberId, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid actingMemberId, CancellationToken ct);
}

internal sealed class ExecutorBindingService(
    ExecutorRegistryDbContext db,
    IProjectAuthorizationService authz,
    IAuditWriter audit) : IExecutorBindingService
{
    public async Task<PagedEnvelopeDto<ExecutorBindingDto>> ListAsync(PageRequest page, CancellationToken ct)
    {
        var q = from b in db.Bindings.AsNoTracking()
                join e in db.Executors.AsNoTracking() on b.ExecutorId equals e.Id
                where b.DeletedAt == null
                select new { b.Id, b.ProjectType, b.ExecutorId, e.Key, e.DisplayName, e.Status, b.CreatedAt };

        var totalCount = await q.CountAsync(ct);

        var rows = await q
            .OrderBy(r => r.ProjectType)
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(ct);

        var dtos = rows.Select(r => new ExecutorBindingDto(
            r.Id, r.ProjectType, r.ExecutorId, r.Key, r.DisplayName, r.Status, r.CreatedAt)).ToList();
        return new PagedEnvelopeDto<ExecutorBindingDto>(dtos,
            new PageMeta(totalCount, page.Page, page.PageSize, page.SortBy, page.SortDir));
    }

    public async Task<ExecutorBindingDto> CreateAsync(CreateBindingRequest req, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "executor-binding:create", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var executor = await db.Executors.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == req.ExecutorId && e.DeletedAt == null, ct)
            ?? throw new NotFoundException("Executor not found.");

        if (await db.Bindings.AnyAsync(b => b.ProjectType == req.ProjectType && b.DeletedAt == null, ct))
            throw new ConflictException($"Project type '{req.ProjectType}' is already bound to an executor.");

        var binding = new ExecutorBinding
        {
            ProjectType = req.ProjectType,
            ExecutorId = req.ExecutorId,
        };
        db.Bindings.Add(binding);

        await audit.WriteAsync(new AuditWriteRequest("ExecutorBinding", binding.Id, "executor-binding:create", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            Details = new Dictionary<string, object?>
            {
                ["projectType"] = req.ProjectType,
                ["executorId"] = req.ExecutorId.ToString(),
                ["executorKey"] = executor.Key,
            },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ExecutorBindingDto(binding.Id, binding.ProjectType, executor.Id, executor.Key,
            executor.DisplayName, executor.Status, binding.CreatedAt);
    }

    public async Task DeleteAsync(Guid id, Guid actingMemberId, CancellationToken ct)
    {
        await authz.EnsureOperatorAsync(actingMemberId, "executor-binding:delete", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var binding = await db.Bindings.FirstOrDefaultAsync(b => b.Id == id && b.DeletedAt == null, ct)
            ?? throw new NotFoundException("Binding not found.");

        binding.DeletedAt = DateTimeOffset.UtcNow;

        await audit.WriteAsync(new AuditWriteRequest("ExecutorBinding", binding.Id, "executor-binding:delete", AuditOutcome.Granted)
        {
            ActingMemberId = actingMemberId,
            Details = new Dictionary<string, object?> { ["projectType"] = binding.ProjectType },
        }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
