using FreezeManager.Domain.Audit;
using FreezeManager.Infrastructure.Audit;
using FreezeManager.Infrastructure.Persistence;
using FreezeManager.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FreezeManager.Infrastructure.Tests.Audit;

public class AuditPersistenceTests
{
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

    private static async Task WriteAsync(TestDatabase database, int count)
    {
        await using var db = database.CreateContext();
        var writer = new AuditWriter(db, Clock);

        for (var i = 0; i < count; i++)
        {
            await writer.AppendAsync("s.sindhe", AuditAction.ChangeRaised, $"CHG-2026-{i:D4}", new { index = i });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Entries_are_numbered_from_one_and_linked_to_the_genesis_hash()
    {
        using var database = new TestDatabase();
        await WriteAsync(database, 3);

        await using var db = database.CreateContext();
        var entries = await db.AuditEntries.OrderBy(e => e.Sequence).ToListAsync();

        Assert.Equal(new long[] { 1, 2, 3 }, entries.Select(e => e.Sequence));
        Assert.Equal(AuditEntry.GenesisHash, entries[0].PreviousHash);
        Assert.Equal(entries[0].Hash, entries[1].PreviousHash);
        Assert.Equal(entries[1].Hash, entries[2].PreviousHash);
    }

    [Fact]
    public async Task Several_entries_written_in_one_unit_of_work_still_chain_correctly()
    {
        // The writer has to see entries staged but not yet saved, or two appends in one operation
        // would both claim the same sequence.
        using var database = new TestDatabase();
        await WriteAsync(database, 5);

        await using var db = database.CreateContext();
        Assert.True((await new AuditReader(db).VerifyAsync()).IsValid);
    }

    [Fact]
    public async Task A_written_log_verifies()
    {
        using var database = new TestDatabase();
        await WriteAsync(database, 10);

        await using var db = database.CreateContext();
        var verification = await new AuditReader(db).VerifyAsync();

        Assert.True(verification.IsValid);
        Assert.Equal(10, verification.EntryCount);
    }

    [Fact]
    public async Task An_empty_log_verifies()
    {
        using var database = new TestDatabase();

        await using var db = database.CreateContext();
        Assert.True((await new AuditReader(db).VerifyAsync()).IsValid);
    }

    [Fact]
    public async Task Updating_an_audit_row_is_refused_outright()
    {
        using var database = new TestDatabase();
        await WriteAsync(database, 3);

        await using var db = database.CreateContext();
        var entry = await db.AuditEntries.FirstAsync(e => e.Sequence == 2);
        entry.Actor = "someone.else";

        await Assert.ThrowsAsync<AuditLogIsAppendOnlyException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_an_audit_row_is_refused_outright()
    {
        using var database = new TestDatabase();
        await WriteAsync(database, 3);

        await using var db = database.CreateContext();
        db.AuditEntries.Remove(await db.AuditEntries.FirstAsync(e => e.Sequence == 2));

        await Assert.ThrowsAsync<AuditLogIsAppendOnlyException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_row_edited_behind_the_interceptors_back_is_still_detected_by_verification()
    {
        // The interceptor stops the application editing history. It cannot stop someone with direct
        // database access, which is what the hash chain is for. Raw SQL is exactly that attack.
        using var database = new TestDatabase();
        await WriteAsync(database, 5);

        await using (var db = database.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE AuditEntries SET Actor = 'mallory' WHERE Sequence = 3");
        }

        await using var verifyDb = database.CreateContext();
        var verification = await new AuditReader(verifyDb).VerifyAsync();

        Assert.False(verification.IsValid);
        Assert.Equal(3, verification.FirstBrokenSequence);
        Assert.Contains("altered", verification.Reason!);
    }

    [Fact]
    public async Task Editing_the_hash_column_alone_does_not_make_a_forgery_verify()
    {
        using var database = new TestDatabase();
        await WriteAsync(database, 4);

        await using (var db = database.CreateContext())
        {
            // Through a parameter: ExecuteSqlRaw reads braces in the SQL text as format placeholders.
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE AuditEntries SET Details = {0} WHERE Sequence = 2", """{"index":99}""");
        }

        await using var verifyDb = database.CreateContext();
        Assert.False((await new AuditReader(verifyDb).VerifyAsync()).IsValid);
    }

    [Fact]
    public async Task Deleting_a_row_behind_the_interceptors_back_breaks_the_sequence()
    {
        using var database = new TestDatabase();
        await WriteAsync(database, 5);

        await using (var db = database.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM AuditEntries WHERE Sequence = 3");
        }

        await using var verifyDb = database.CreateContext();
        var verification = await new AuditReader(verifyDb).VerifyAsync();

        Assert.False(verification.IsValid);
        Assert.Equal(4, verification.FirstBrokenSequence);
    }

    [Fact]
    public async Task The_log_can_be_read_back_for_one_subject()
    {
        using var database = new TestDatabase();
        await WriteAsync(database, 6);

        await using var db = database.CreateContext();
        var entries = await new AuditReader(db).ReadAsync("CHG-2026-0003");

        var only = Assert.Single(entries);
        Assert.Equal("CHG-2026-0003", only.Subject);
    }
}
