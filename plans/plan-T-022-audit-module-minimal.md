# Implementation Plan: T-022 — Audit module (minimal) + IAuditWriter

## Task Reference
- **Task ID:** T-022
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** S
- **Rationale:** FEAT-002 AC-2 (denies audited), AC-4 (soft delete audited), AC-5 (deny test required). Every mutation in T-024 calls `IAuditWriter.WriteAsync` inside the same transaction.

## Overview
Replace T-009's empty Audit migration with the real `AuditEntry` table + an `IAuditWriter` contract that other modules consume from `DevHub.Contracts`. Audit module remains module-private otherwise (query endpoints + operator dashboard land in FEAT-006).

## Implementation Steps

### Step 1: Contract types
**Files:**
- `src/DevHub.Contracts/Audit/AuditOutcome.cs`
- `src/DevHub.Contracts/Audit/AuditWriteRequest.cs`
- `src/DevHub.Contracts/Audit/IAuditWriter.cs`
**Action:** Create

```csharp
public enum AuditOutcome { Granted = 0, Denied = 1, Failed = 2 }

public sealed record AuditWriteRequest(
    string TargetType, Guid? TargetId, string Action, AuditOutcome Outcome)
{
    public Guid? ActingMemberId { get; init; }
    public Guid? ProjectId { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public IReadOnlyDictionary<string, object?>? Details { get; init; }
}

public interface IAuditWriter
{
    Task WriteAsync(AuditWriteRequest request, CancellationToken ct = default);
}
```

`AuditWriteRequest` is a record so callers can use `with { }` to clone for variants.

### Step 2: Entity + enum
**Files:**
- `src/DevHub.Modules.Audit/Entities/Enums/AuditOutcome.cs` (alias the Contracts enum)
- `src/DevHub.Modules.Audit/Entities/AuditEntry.cs`
**Action:** Create

`AuditEntry : BaseEntity` per data-model.md §AuditEntry: `OccurredAt`, `ActingMemberId`, `ProjectId`, `TargetType`, `TargetId`, `Action`, `Outcome` (string-mapped enum from `DevHub.Contracts.Audit.AuditOutcome`), `Reason`, `DetailsJson` (string, stored as `jsonb` via `HasColumnType("jsonb")`).

### Step 3: DbContext mappings
**File:** `src/DevHub.Modules.Audit/AuditDbContext.cs`
**Action:** Modify

```csharp
public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasDefaultSchema(SchemaName);
    modelBuilder.Entity<AuditEntry>(b =>
    {
        b.Property(a => a.TargetType).HasMaxLength(60).IsRequired();
        b.Property(a => a.Action).HasMaxLength(60).IsRequired();
        b.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(a => a.Reason).HasMaxLength(1000);
        b.Property(a => a.DetailsJson).HasColumnType("jsonb");
        b.HasIndex(a => new { a.ProjectId, a.OccurredAt }).IsDescending(false, true);
        b.HasIndex(a => new { a.ActingMemberId, a.OccurredAt }).IsDescending(false, true);
        b.HasIndex(a => new { a.Outcome, a.OccurredAt }).IsDescending(false, true);
    });
    base.OnModelCreating(modelBuilder);
}
```

### Step 4: Replace T-009's empty migration
**Action:** Generate
1. `dotnet ef migrations remove --project src/DevHub.Modules.Audit --startup-project src/DevHub.Api --context AuditDbContext` (drops T-009's `Initial`).
2. `dotnet ef migrations add Initial --project src/DevHub.Modules.Audit --startup-project src/DevHub.Api --context AuditDbContext`.

The new migration's `Up()` should: `EnsureSchema("audit")`, then `CreateTable("audit_entries", ...)` with all columns + the three indexes from Step 3.

### Step 5: AuditWriter implementation
**File:** `src/DevHub.Modules.Audit/Services/AuditWriter.cs`
**Action:** Create

```csharp
internal sealed class AuditWriter(AuditDbContext db) : IAuditWriter
{
    public async Task WriteAsync(AuditWriteRequest req, CancellationToken ct = default)
    {
        var entry = new AuditEntry
        {
            OccurredAt = req.OccurredAt ?? DateTimeOffset.UtcNow,
            ActingMemberId = req.ActingMemberId,
            ProjectId = req.ProjectId,
            TargetType = req.TargetType,
            TargetId = req.TargetId,
            Action = req.Action,
            Outcome = req.Outcome,
            Reason = req.Reason,
            DetailsJson = req.Details is null ? null : JsonSerializer.Serialize(req.Details),
        };
        db.AuditEntries.Add(entry);
        // If a caller has opened an outer transaction (typical for services that audit alongside their own mutation),
        // they're responsible for SaveChangesAsync. If not, save here.
        if (db.Database.CurrentTransaction is null) await db.SaveChangesAsync(ct);
    }
}
```

The `Add` + conditional `SaveChangesAsync` pattern lets a Workspace service do:
```csharp
using var tx = await _db.Database.BeginTransactionAsync(ct);
_db.Teams.Add(team);
await _audit.WriteAsync(req, ct);  // stages only
await _db.SaveChangesAsync(ct);    // commits both
await tx.CommitAsync(ct);
```
…with both rows landing or both rolling back.

### Step 6: Wire DI
**File:** `src/DevHub.Modules.Audit/AuditModuleExtensions.cs`
**Action:** Modify

```csharp
services.AddScoped<IAuditWriter, AuditWriter>();
```

### Step 7: Test
**File:** `tests/DevHub.Modules.Audit.Tests/AuditWriterTests.cs`
**Action:** Create

Three tests using the `[Collection("postgres")]` harness:
1. **Default outcome flushes immediately** — call `WriteAsync({TargetType="Team", Action="team:create", Outcome=Granted})`, then open a new DbContext and assert the row exists.
2. **Inside an outer transaction, audit row commits or rolls back with the caller's other writes.**
3. **`Denied` + `Failed` outcomes round-trip with `Reason` + `DetailsJson`.**

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `src/DevHub.Contracts/Audit/*.cs` | Create | `AuditOutcome`, `AuditWriteRequest`, `IAuditWriter` |
| `src/DevHub.Modules.Audit/Entities/AuditEntry.cs` + enum | Create | Entity per data-model |
| `src/DevHub.Modules.Audit/AuditDbContext.cs` | Modify | DbSet + mappings + indexes |
| `src/DevHub.Modules.Audit/Migrations/*` | Replace | Real `Initial` migration |
| `src/DevHub.Modules.Audit/Services/AuditWriter.cs` | Create | Implementation |
| `src/DevHub.Modules.Audit/AuditModuleExtensions.cs` | Modify | Register `IAuditWriter` |
| `tests/DevHub.Modules.Audit.Tests/AuditWriterTests.cs` | Create | 3 tests + harness wiring |

## Edge Cases & Risks
- **Concurrent multi-instance writers** — `AuditEntry.Id` is a Guid generated client-side; no row-id contention. No INSERT race.
- **Outer-transaction misuse** — if a caller opens a transaction but never calls `SaveChangesAsync`, the audit row is lost. Mitigation: integration test in T-025 asserts audit presence after every mutation; reviewers catch the gap.
- **`DetailsJson` size** — leave it `jsonb` without a hard size cap; v1 callers should keep payloads small (a few hundred bytes). Add a `MaxLength` only if abuse appears.

## Acceptance Verification
- [ ] `dotnet ef migrations list --project src/DevHub.Modules.Audit` shows exactly one migration (replacing the T-009 empty one).
- [ ] `dotnet ef database update --project src/DevHub.Modules.Audit --startup-project src/DevHub.Api` applies cleanly on an empty DB.
- [ ] `AuditWriterTests` (3) pass under `dotnet test`.
- [ ] An outer transaction commits both the mutation and the audit row, or rolls back both, atomically (asserted in test #2).
