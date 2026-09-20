using FreezeManager.Domain.Audit;
using FreezeManager.Domain.Overrides;
using FreezeManager.Domain.Services;
using FreezeManager.Infrastructure.Audit;
using FreezeManager.Infrastructure.Overrides;
using FreezeManager.Infrastructure.Persistence;
using FreezeManager.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FreezeManager.Infrastructure.Tests.Overrides;

public class OverrideSweeperTests
{
    private static readonly DateTimeOffset Granted = new(2026, 5, 8, 14, 0, 0, TimeSpan.Zero);

    private static FreezeOverrideRecord GrantedRecord(bool breakGlass = false, DateTimeOffset? grantedAt = null)
    {
        var at = grantedAt ?? Granted;

        return new FreezeOverrideRecord
        {
            ChangeReference = "CHG-2026-0001",
            IncidentReference = "INC-1234",
            Justification = "Telemetry ingest is dropping packets mid-session and the standby failed over.",
            RequestedBy = "s.sindhe",
            RequestedAtUtc = at.UtcDateTime,
            GrantDurationMinutes = 120,
            IsBreakGlass = breakGlass,
            State = OverrideState.Granted,
            GrantedAtUtc = at.UtcDateTime,
            ExpiresAtUtc = at.AddHours(2).UtcDateTime,
            RetrospectiveDueAtUtc = breakGlass ? at.AddHours(24).UtcDateTime : null
        };
    }

    private static async Task<SweepResult> SweepAt(TestDatabase database, DateTimeOffset now)
    {
        await using var db = database.CreateContext();
        var clock = new FixedTimeProvider(now);
        return await new OverrideExpirySweeper(db, new AuditWriter(db, clock), clock).SweepAsync();
    }

    [Fact]
    public async Task A_grant_that_has_not_elapsed_is_left_alone()
    {
        using var database = new TestDatabase();

        await using (var db = database.CreateContext())
        {
            db.FreezeOverrides.Add(GrantedRecord());
            await db.SaveChangesAsync();
        }

        var result = await SweepAt(database, Granted.AddHours(1));

        Assert.Equal(0, result.Expired);
        Assert.False(result.DidAnything);
    }

    [Fact]
    public async Task An_elapsed_grant_expires_and_the_expiry_is_written_to_the_audit_log()
    {
        using var database = new TestDatabase();

        await using (var db = database.CreateContext())
        {
            db.FreezeOverrides.Add(GrantedRecord());
            await db.SaveChangesAsync();
        }

        var result = await SweepAt(database, Granted.AddHours(3));

        Assert.Equal(1, result.Expired);

        await using var verify = database.CreateContext();
        var stored = await verify.FreezeOverrides.SingleAsync();
        Assert.Equal(OverrideState.Expired, stored.State);

        var entry = Assert.Single(await verify.AuditEntries.Where(e => e.Action == AuditAction.OverrideExpired).ToListAsync());
        Assert.Equal("CHG-2026-0001", entry.Subject);
        Assert.Equal(OverrideExpirySweeper.SweeperActor, entry.Actor);
        Assert.Contains("INC-1234", entry.Details);
    }

    [Fact]
    public async Task Sweeping_repeatedly_writes_exactly_one_expiry_entry()
    {
        // The sweep runs on a timer. One expiry means one audit entry, not one per minute forever.
        using var database = new TestDatabase();

        await using (var db = database.CreateContext())
        {
            db.FreezeOverrides.Add(GrantedRecord());
            await db.SaveChangesAsync();
        }

        await SweepAt(database, Granted.AddHours(3));
        await SweepAt(database, Granted.AddHours(4));
        await SweepAt(database, Granted.AddHours(5));

        await using var verify = database.CreateContext();
        Assert.Equal(1, await verify.AuditEntries.CountAsync(e => e.Action == AuditAction.OverrideExpired));
    }

    [Fact]
    public async Task An_overdue_break_glass_retrospective_is_flagged_once()
    {
        using var database = new TestDatabase();

        await using (var db = database.CreateContext())
        {
            db.FreezeOverrides.Add(GrantedRecord(breakGlass: true));
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, (await SweepAt(database, Granted.AddHours(23))).RetrospectivesOverdue);

        var result = await SweepAt(database, Granted.AddHours(25));
        Assert.Equal(1, result.RetrospectivesOverdue);

        await SweepAt(database, Granted.AddHours(26));

        await using var verify = database.CreateContext();
        var entries = await verify.AuditEntries.Where(e => e.Action == AuditAction.RetrospectiveOverdue).ToListAsync();

        var only = Assert.Single(entries);
        Assert.Contains("dueAtUtc", only.Details);
    }

    [Fact]
    public async Task A_completed_retrospective_is_never_flagged_overdue()
    {
        using var database = new TestDatabase();

        await using (var db = database.CreateContext())
        {
            var record = GrantedRecord(breakGlass: true);
            record.RetrospectiveCompletedAtUtc = Granted.AddHours(6).UtcDateTime;
            record.RetrospectiveNotes = "Root cause was an expired certificate on the standby node; renewal automated.";
            db.FreezeOverrides.Add(record);
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, (await SweepAt(database, Granted.AddHours(48))).RetrospectivesOverdue);
    }

    [Fact]
    public async Task The_audit_log_still_verifies_after_the_sweeper_has_written_to_it()
    {
        using var database = new TestDatabase();

        await using (var db = database.CreateContext())
        {
            db.FreezeOverrides.Add(GrantedRecord(breakGlass: true));
            await db.SaveChangesAsync();
        }

        await SweepAt(database, Granted.AddHours(25));

        await using var verify = database.CreateContext();
        Assert.True((await new AuditReader(verify).VerifyAsync()).IsValid);
    }
}
