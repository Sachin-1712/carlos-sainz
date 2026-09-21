using FreezeManager.Domain.Calendar;
using FreezeManager.Infrastructure.CalendarSync;
using FreezeManager.Infrastructure.CalendarSync.Seed;
using FreezeManager.Infrastructure.Persistence;
using FreezeManager.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FreezeManager.Infrastructure.Tests.CalendarSync;

public class SeedExportAndDriftTests
{
    private static readonly DateTime Fp1 = new(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));

    private static async Task SyncAsync(
        TestDatabase database,
        CalendarSource source,
        params RaceEventRecord[] events)
    {
        await using var db = database.CreateContext();
        var provider = StubCalendarProvider.Returning(source.ToString(), source, events);
        await new CalendarSyncService(db, provider, Clock).SyncSeasonAsync(2026);
    }

    private static Task SyncAsync(TestDatabase database, params RaceEventRecord[] events) =>
        SyncAsync(database, CalendarSource.LiveUpstream, events);

    private static string WriteSeed(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"seed-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    // ------------------------------------------------------------------ export

    [Fact]
    public async Task An_exported_seed_reloads_to_the_same_calendar()
    {
        using var database = new TestDatabase();
        await SyncAsync(database,
            StubCalendarProvider.Weekend(1, Fp1),
            StubCalendarProvider.Weekend(2, Fp1.AddDays(7)),
            StubCalendarProvider.Weekend(3, Fp1.AddDays(14)));

        await using var db = database.CreateContext();
        var export = await SeedExporter.BuildAsync(db, 2026);

        var path = WriteSeed(export.Json);

        try
        {
            var reloaded = await new SeedCalendarProvider(path).FetchSeasonAsync(2026);

            Assert.Equal(3, reloaded.Events.Count);
            Assert.Equal(new[] { 1, 2, 3 }, reloaded.Events.Select(e => e.Round));

            // The round trip has to preserve the times the freeze is computed from.
            var original = await db.RaceEvents.Include(e => e.Sessions)
                .SingleAsync(e => e.Round == 1);

            var roundTripped = reloaded.Events.Single(e => e.Round == 1);

            Assert.Equal(
                original.Sessions.OrderBy(s => s.Type).Select(s => s.ScheduledStartUtc),
                roundTripped.Sessions.OrderBy(s => s.Type).Select(s => s.ScheduledStartUtc));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_seed_exported_from_a_verified_sync_marks_every_row_verified()
    {
        using var database = new TestDatabase();
        await SyncAsync(database, StubCalendarProvider.Weekend(1, Fp1));

        await using var db = database.CreateContext();
        var export = await SeedExporter.BuildAsync(db, 2026);

        Assert.True(export.AllVerified);
        Assert.All(export.Document.Events!, e => Assert.True(e.Verified));
        Assert.Contains("verified: true", export.Document.Disclaimer!);
    }

    [Fact]
    public async Task A_seed_exported_from_unverified_data_says_so_rather_than_claiming_otherwise()
    {
        using var database = new TestDatabase();
        await SyncAsync(database, CalendarSource.Seed, StubCalendarProvider.Weekend(1, Fp1, CalendarSource.Seed));

        await using var db = database.CreateContext();
        var export = await SeedExporter.BuildAsync(db, 2026);

        Assert.False(export.AllVerified);
        Assert.All(export.Document.Events!, e => Assert.False(e.Verified));
    }

    [Fact]
    public async Task A_manually_entered_parc_ferme_window_survives_the_export_round_trip()
    {
        // Decision 13 keeps a person's correction through a sync; it has to survive a seed
        // regeneration too, or refreshing the fallback quietly discards their work.
        using var database = new TestDatabase();
        await SyncAsync(database, StubCalendarProvider.Weekend(1, Fp1));

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

        await using var exportDb = database.CreateContext();
        var path = WriteSeed((await SeedExporter.BuildAsync(exportDb, 2026)).Json);

        try
        {
            var reloaded = await new SeedCalendarProvider(path).FetchSeasonAsync(2026);
            var windows = reloaded.Events.Single().ParcFermeWindows;

            Assert.Contains(windows, w => !w.IsDerived && w.Label == "Entered by duty manager");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ------------------------------------------------------------------ drift

    [Fact]
    public async Task A_seed_matching_the_last_verified_sync_reports_clean()
    {
        using var database = new TestDatabase();
        await SyncAsync(database,
            StubCalendarProvider.Weekend(1, Fp1),
            StubCalendarProvider.Weekend(2, Fp1.AddDays(7)));

        await using var db = database.CreateContext();
        var path = WriteSeed((await SeedExporter.BuildAsync(db, 2026)).Json);

        try
        {
            var report = await SeedDriftCheck.InspectAsync(db, path, 2026);

            Assert.True(report.IsClean);
            Assert.False(report.HasDrifted);
            Assert.Equal(2, report.SeedRoundCount);
            Assert.Equal(2, report.LastVerifiedRoundCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_seed_with_a_different_round_count_is_reported_as_drifted()
    {
        // This is the real failure: the seed describes a season that changed under it, so a cold
        // start computes freeze windows for races that are not happening and misses ones that are.
        using var database = new TestDatabase();
        await SyncAsync(database, StubCalendarProvider.Weekend(1, Fp1));

        await using var db = database.CreateContext();
        var staleExport = await SeedExporter.BuildAsync(db, 2026);

        // Now the season grows, but the seed on disk does not.
        await SyncAsync(database,
            StubCalendarProvider.Weekend(1, Fp1),
            StubCalendarProvider.Weekend(2, Fp1.AddDays(7)),
            StubCalendarProvider.Weekend(3, Fp1.AddDays(14)));

        var path = WriteSeed(staleExport.Json);

        try
        {
            await using var freshDb = database.CreateContext();
            var report = await SeedDriftCheck.InspectAsync(freshDb, path, 2026);

            Assert.True(report.HasDrifted);
            Assert.Equal(1, report.SeedRoundCount);
            Assert.Equal(3, report.LastVerifiedRoundCount);
            Assert.Contains("seed-export", report.Problem!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Cancelled_rounds_do_not_count_toward_the_seed_total()
    {
        // A cancelled round is carried in the file but is not a race that needs freezing, so it
        // must not make a stale seed look like it matches.
        using var database = new TestDatabase();

        var cancelled = StubCalendarProvider.Weekend(2, Fp1.AddDays(7));
        cancelled.Status = EventStatus.Cancelled;

        await SyncAsync(database, StubCalendarProvider.Weekend(1, Fp1), cancelled);

        await using var db = database.CreateContext();
        var export = await SeedExporter.BuildAsync(db, 2026);

        Assert.Equal(1, export.ActiveRoundCount);
        Assert.Equal(2, export.Document.Events!.Count);

        var path = WriteSeed(export.Json);

        try
        {
            Assert.True((await SeedDriftCheck.InspectAsync(db, path, 2026)).IsClean);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task With_no_verified_sync_there_is_nothing_to_compare_against_and_that_is_said_plainly()
    {
        using var database = new TestDatabase();
        await SyncAsync(database, CalendarSource.Seed, StubCalendarProvider.Weekend(1, Fp1, CalendarSource.Seed));

        await using var db = database.CreateContext();
        var path = WriteSeed((await SeedExporter.BuildAsync(db, 2026)).Json);

        try
        {
            var report = await SeedDriftCheck.InspectAsync(db, path, 2026);

            Assert.False(report.HasDrifted);
            Assert.Contains("nothing to compare", report.Problem!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_missing_seed_file_is_reported_rather_than_throwing()
    {
        using var database = new TestDatabase();

        await using var db = database.CreateContext();
        var report = await SeedDriftCheck.InspectAsync(db, "/no/such/seed.json", 2026);

        Assert.False(report.IsClean);
        Assert.Contains("No seed file", report.Problem!);
    }

    // ------------------------------------------------------------------ the shipped seed

    [Fact]
    public async Task The_shipped_seed_declares_itself_stale_until_it_is_regenerated()
    {
        // The bundled file predates the 2026 calendar changes and cannot be corrected from here,
        // so it has to say so rather than look authoritative. Regenerating it is one command.
        var result = await new SeedCalendarProvider(Fixtures.SeedCalendarPath).FetchSeasonAsync(2026);

        Assert.All(result.Events, e => Assert.False(e.IsVerified));
        Assert.Contains(result.Events, e => e.Status == EventStatus.Cancelled);
    }
}
