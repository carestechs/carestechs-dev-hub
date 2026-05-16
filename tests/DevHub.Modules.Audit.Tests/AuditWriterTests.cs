using DevHub.Contracts.Audit;
using DevHub.Modules.Audit.Services;
using DevHub.TestHarness;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Audit.Tests;

[Collection("postgres")]
public class AuditWriterTests(PostgresFixture pg)
{
    private async Task<AuditDbContext> NewContextAsync()
    {
        var connStr = await pg.CreateIsolatedDatabaseAsync($"audit_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(connStr)
            .UseSnakeCaseNamingConvention()
            .Options;
        var db = new AuditDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    [Fact]
    public async Task Write_outside_a_transaction_persists_immediately()
    {
        await using var db = await NewContextAsync();
        var sut = new AuditWriter(db);

        await sut.WriteAsync(new AuditWriteRequest("Team", Guid.NewGuid(), "team:create", AuditOutcome.Granted)
        {
            ActingMemberId = Guid.NewGuid(),
            Reason = null,
        });

        // A fresh context sees the row.
        await using var fresh = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(db.Database.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);
        (await fresh.AuditEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Inside_an_outer_transaction_the_write_commits_with_the_callers_save()
    {
        await using var db = await NewContextAsync();
        var sut = new AuditWriter(db);

        await using var tx = await db.Database.BeginTransactionAsync();
        await sut.WriteAsync(new AuditWriteRequest("Team", Guid.NewGuid(), "team:create", AuditOutcome.Granted));

        // Before commit, an isolated context cannot see the row yet (read committed isolation).
        await using (var snoop = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(db.Database.GetConnectionString()).UseSnakeCaseNamingConvention().Options))
        {
            (await snoop.AuditEntries.CountAsync()).Should().Be(0);
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await using var fresh = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(db.Database.GetConnectionString()).UseSnakeCaseNamingConvention().Options);
        (await fresh.AuditEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Inside_an_outer_transaction_the_write_rolls_back_with_the_caller()
    {
        await using var db = await NewContextAsync();
        var sut = new AuditWriter(db);

        await using var tx = await db.Database.BeginTransactionAsync();
        await sut.WriteAsync(new AuditWriteRequest("Team", Guid.NewGuid(), "team:create", AuditOutcome.Granted));
        await db.SaveChangesAsync(); // commits to the transaction only
        await tx.RollbackAsync();

        await using var fresh = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(db.Database.GetConnectionString()).UseSnakeCaseNamingConvention().Options);
        (await fresh.AuditEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Denied_and_Failed_outcomes_roundtrip_with_reason_and_details()
    {
        await using var db = await NewContextAsync();
        var sut = new AuditWriter(db);

        var memberId = Guid.NewGuid();
        await sut.WriteAsync(new AuditWriteRequest("WorkItem", null, "checkpoint:signal", AuditOutcome.Denied)
        {
            ActingMemberId = memberId,
            ProjectId = Guid.NewGuid(),
            Reason = "member lacks role 'approver'",
            Details = new Dictionary<string, object?> { ["requiredRoleKey"] = "approver" },
        });

        await using var fresh = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(db.Database.GetConnectionString()).UseSnakeCaseNamingConvention().Options);
        var row = await fresh.AuditEntries.SingleAsync();
        row.Outcome.Should().Be(AuditOutcome.Denied);
        row.Reason.Should().Be("member lacks role 'approver'");
        // Postgres jsonb re-normalizes whitespace; strip it before comparing.
        row.DetailsJson.Should().NotBeNull();
        row.DetailsJson!.Replace(" ", "").Should().Contain("\"requiredRoleKey\":\"approver\"");
        row.ActingMemberId.Should().Be(memberId);
    }
}
