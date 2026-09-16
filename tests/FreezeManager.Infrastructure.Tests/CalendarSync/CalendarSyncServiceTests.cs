using FreezeManager.Domain.Calendar;
using FreezeManager.Domain.Freeze;
using FreezeManager.Domain.Services;
using FreezeManager.Infrastructure.CalendarSync;
using FreezeManager.Infrastructure.CalendarSync.Seed;
using FreezeManager.Infrastructure.Persistence;
using FreezeManager.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FreezeManager.Infrastructure.Tests.CalendarSync;

public class CalendarSyncServiceTests
{
    private static readonly DateTime Fp1 = new(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));

    private static async Task<CalendarSyncResult> Sync(TestDatabase database, IRaceCalendarProvider provider)
    {
        await using var db = database.CreateContext();
        return await new CalendarSyncService(db, provider, Clock).SyncSeasonAsync(2026);
    }

    private static async Task<RaceEventRecord> Load(TestDatabase database, int round)
    {
        await using var db = database.CreateContext();
        return await db.RaceEvents
            .Include(e => e.Sessions)
            .Include(e => e.ParcFermeWindows)
            .SingleAsync(e => e.Round == round);
    }

    [Fact]
    public async Task A_first_sync_adds_every_event_and_records_the_run()
    {
        using var database = new TestDatabase();
        var provider = StubCalendarProvider.Returning("live", CalendarSource.LiveUpstream,
            StubCalendarProvider.Weekend(1, Fp1),
            StubCalendarProvider.Weekend(2, Fp1.AddDays(7)));

        var result = await Sync(database, provider);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Updated);

        await using var db = database.CreateContext();
        var run = Assert.Single(await db.CalendarSyncRuns.ToListAsync());
        Assert.True(run.Succeeded);
        Assert.Equal(2, run.EventsAdded);
        Assert.Equal(CalendarSource.LiveUpstream, run.Source);
        Assert.NotNull(run.CompletedAtUtc);
    }

    [Fact]
    public async Task A_second_sync_updates_rather_than_duplicates()
    {
        using var database = new TestDatabase();
        var provider = StubCalendarProvider.Returning("live", CalendarSource.LiveUpstream, StubCalendarProvider.Weekend(1, Fp1));

        await Sync(database, provider);
        var second = await Sync(database, provider);

        Assert.Equal(0, second.Added);
        Assert.Equal(1, second.Updated);

        await using var db = database.CreateContext();
        Assert.Equal(1, await db.RaceEvents.CountAsync());
        Assert.Equal(3, await db.Sessions.CountAsync());
    }

    [Fact]
    public async Task A_rescheduled_session_upstream_moves_the_stored_scheduled_time()
    {
        using var database = new TestDatabase();

        await Sync(database, StubCalendarProvider.Returning("live", CalendarSource.LiveUpstream, StubCalendarProvider.Weekend(1, Fp1)));
        await Sync(database, StubCalendarProvider.Returning("live", CalendarSource.LiveUpstream, StubCalendarProvider.Weekend(1, Fp1.AddHours(2))));

        var stored = await Load(database, 1);
        var fp1 = stored.Sessions.Single(s => s.Type == SessionType.Practice1);

        Assert.Equal(Fp1.AddHours(2), fp1.ScheduledStartUtc);
    }

    [Fact]
    public async Task Actual_times_recorded_by_a_person_survive_the_next_sync()
    {
        using var database = new TestDatabase();
        var provider = StubCalendarProvider.Returning("live", CalendarSource.LiveUpstream, StubCalendarProvider.Weekend(1, Fp1));
        await Sync(database, provider);

        var redFlagDelay = Fp1.AddHours(48).AddMinutes(45);

        await using (var db = database.CreateContext())
        {
            var raceRow = await db.Sessions.SingleAsync(s => s.Type == SessionType.Race);
            raceRow.ActualStartUtc = redFlagDelay;
            await db.SaveChangesAsync();
        }

        await Sync(database, provider);

        var stored = await Load(database, 1);
        var race = stored.Sessions.Single(s => s.Type == SessionType.Race);

        Assert.Equal(redFlagDelay, race.ActualStartUtc);
        Assert.Equal(DateTimeKind.Utc, race.ActualStartUtc!.Value.Kind);
    }

    [Fact]
    public async Task A_pinned_event_is_left_alone_and_counted()
    {
        using var database = new TestDatabase();
        await Sync(database, StubCalendarProvider.Returning("live", CalendarSource.LiveUpstream, StubCalendarProvider.Weekend(1, Fp1)));

        await using (var db = database.CreateContext())
        {
            var stored = await db.RaceEvents.SingleAsync();
            stored.PinnedByAdmin = true;
            await db.SaveChangesAsync();
        }

        var result = await Sync(database, StubCalendarProvider.Returning("live", CalendarSource.LiveUpstream, StubCalendarProvider.Weekend(1, Fp1.AddHours(2))));

        Assert.Equal(1, result.SkippedPinned);
        Assert.Equal(0, result.Updated);

        var unchanged = await Load(database, 1);
        Assert.Equal(Fp1, unchanged.Sessions.Single(s => s.Type == SessionType.Practice1).ScheduledStartUtc);
    }

    [Fact]
    public async Task A_manually_entered_parc_ferme_window_survives_while_derived_ones_are_replaced()
    {
        using var database = new TestDatabase();
        var provider = StubCalendarProvider.Returning("live", CalendarSource.LiveUpstream, StubCalendarProvider.Weekend(1, Fp1));
        await Sync(database, provider);

        await using (var db = database.CreateContext())
        {
            var stored = await db.RaceEvents.Include(e => e.ParcFermeWindows).SingleAsync();
            stored.ParcFermeWindows.Add(new ParcFermeWindowRecord
            {
                StartUtc = Fp1.AddHours(20),
                EndUtc = Fp1.AddHours(22),
                Label = "Entered by duty manager",
                IsDerived = false
            });
            await db.SaveChangesAsync();
        }

        await Sync(database, provider);

        var after = await Load(database, 1);
        Assert.Equal(2, after.ParcFermeWindows.Count);
        Assert.Single(after.ParcFermeWindows, w => !w.IsDerived && w.Label == "Entered by duty manager");
        Assert.Single(after.ParcFermeWindows, w => w.IsDerived);
    }

    [Fact]
    public async Task A_session_upstream_no_longer_lists_is_removed()
    {
        using var database = new TestDatabase();
        await Sync(database, StubCalendarProvider.Returning("live", CalendarSource.LiveUpstream, StubCalendarProvider.Weekend(1, Fp1)));

        var trimmed = StubCalendarProvider.Weekend(1, Fp1);
        trimmed.Sessions.RemoveAll(s => s.Type == SessionType.Qualifying);
        trimmed.ParcFermeWindows = ParcFermeDerivation.Derive(trimmed.Sessions, IngestionDefaults.Standard);

        await Sync(database, StubCalendarProvider.Returning("live", CalendarSource.LiveUpstream, trimmed));

        var stored = await Load(database, 1);
        Assert.Equal(2, stored.Sessions.Count);
        Assert.DoesNotContain(stored.Sessions, s => s.Type == SessionType.Qualifying);
    }

    [Fact]
    public async Task A_failed_fetch_is_recorded_as_a_failed_run_and_changes_nothing()
    {
        using var database = new TestDatabase();
        var provider = StubCalendarProvider.Throwing("live", new HttpRequestException("timeout"));

        var result = await Sync(database, provider);

        Assert.False(result.Succeeded);
        Assert.Contains("timeout", result.Error!);

        await using var db = database.CreateContext();
        var run = Assert.Single(await db.CalendarSyncRuns.ToListAsync());
        Assert.False(run.Succeeded);
        Assert.Contains("timeout", run.Message!);
        Assert.Equal(0, await db.RaceEvents.CountAsync());
    }

    [Fact]
    public async Task Seed_to_store_to_domain_to_engine_round_trip_freezes_trackside_during_the_first_weekend()
    {
        // The whole Phase 2 pipeline in one line of intent: load the bundled seed, persist it, read it
        // back as a domain calendar, and ask the engine about the season's first FP1.
        using var database = new TestDatabase();
        var result = await Sync(database, new SeedCalendarProvider(Fixtures.SeedCalendarPath, time: Clock));
        Assert.Equal(24, result.Added);

        await using var db = database.CreateContext();
        var calendar = await new CalendarRepository(db).LoadSeasonAsync(2026);
        Assert.Equal(24, calendar.Events.Count);

        var firstFp1 = calendar.Events[0].FirstSessionStartUtc!.Value;
        var evaluation = new FreezeCalculator(calendar).Evaluate(ServiceTier.Trackside, firstFp1);

        Assert.True(evaluation.IsFrozen);
        Assert.Contains("Round 1", evaluation.Reason!);
    }

    [Fact]
    public async Task Loading_a_season_with_nothing_stored_gives_an_empty_calendar_not_an_error()
    {
        using var database = new TestDatabase();

        await using var db = database.CreateContext();
        var repository = new CalendarRepository(db);

        Assert.False(await repository.HasSeasonAsync(2026));
        var calendar = await repository.LoadSeasonAsync(2026);
        Assert.Empty(calendar.Events);
    }
}
