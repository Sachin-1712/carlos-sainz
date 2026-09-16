using FreezeManager.Domain.Calendar;
using FreezeManager.Infrastructure.CalendarSync;
using FreezeManager.Infrastructure.CalendarSync.Ergast;
using FreezeManager.Infrastructure.Persistence;
using FreezeManager.Infrastructure.Tests.Support;
using Xunit;

namespace FreezeManager.Infrastructure.Tests.CalendarSync;

public class ErgastCalendarMapperTests
{
    private static readonly DateTimeOffset FetchedAt = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private static CalendarFetchResult MapSample() =>
        ErgastCalendarMapper.Map(2026, Fixtures.ErgastSample(), IngestionDefaults.Standard, FetchedAt, "test");

    [Fact]
    public void Maps_every_race_that_has_published_times_and_skips_the_one_that_does_not()
    {
        var result = MapSample();

        Assert.Equal(new[] { 1, 2, 4 }, result.Events.Select(e => e.Round));
        Assert.Contains(result.Warnings, w => w.StartsWith("Round 3:", StringComparison.Ordinal) && w.Contains("skipped"));
    }

    [Fact]
    public void A_conventional_weekend_gets_five_sessions_with_the_race_taken_from_the_top_level_date_and_time()
    {
        var australia = MapSample().Events.Single(e => e.Round == 1);

        Assert.Equal(EventFormat.Conventional, australia.Format);
        Assert.Equal(5, australia.Sessions.Count);

        var race = australia.Sessions.Single(s => s.Type == SessionType.Race);
        Assert.Equal(new DateTime(2026, 3, 8, 4, 0, 0, DateTimeKind.Utc), race.ScheduledStartUtc);
        Assert.Equal(race.ScheduledStartUtc + IngestionDefaults.Standard.RaceDuration, race.ScheduledEndUtc);
        Assert.Equal(DateTimeKind.Utc, race.ScheduledStartUtc.Kind);
    }

    [Fact]
    public void A_sprint_weekend_is_detected_from_its_sessions_and_gets_two_derived_parc_ferme_windows()
    {
        var china = MapSample().Events.Single(e => e.Round == 2);

        Assert.Equal(EventFormat.Sprint, china.Format);
        Assert.Contains(china.Sessions, s => s.Type == SessionType.SprintQualifying);
        Assert.Contains(china.Sessions, s => s.Type == SessionType.Sprint);
        Assert.DoesNotContain(china.Sessions, s => s.Type == SessionType.Practice3);

        Assert.Equal(2, china.ParcFermeWindows.Count);
        Assert.All(china.ParcFermeWindows, w => Assert.True(w.IsDerived));
    }

    [Fact]
    public void A_conventional_weekend_gets_one_derived_parc_ferme_window_from_qualifying_to_release()
    {
        var australia = MapSample().Events.Single(e => e.Round == 1);

        var window = Assert.Single(australia.ParcFermeWindows);
        var qualifying = australia.Sessions.Single(s => s.Type == SessionType.Qualifying);
        var race = australia.Sessions.Single(s => s.Type == SessionType.Race);

        Assert.Equal(qualifying.ScheduledStartUtc, window.StartUtc);
        Assert.Equal(race.ScheduledEndUtc + IngestionDefaults.Standard.ParcFermeReleaseAfterRace, window.EndUtc);
        Assert.True(window.IsDerived);
    }

    [Fact]
    public void Known_circuits_resolve_to_their_time_zone()
    {
        var australia = MapSample().Events.Single(e => e.Round == 1);

        Assert.Equal("Australia/Melbourne", australia.LocalTimeZoneId);
        Assert.Equal("albert_park", australia.UpstreamCircuitId);
    }

    [Fact]
    public void An_unknown_circuit_falls_back_to_utc_and_says_so()
    {
        var result = MapSample();
        var madrid = result.Events.Single(e => e.Round == 4);

        Assert.Equal(CircuitTimeZones.Unknown, madrid.LocalTimeZoneId);
        Assert.Contains(result.Warnings, w => w.Contains("madring") && w.Contains("time zone"));
    }

    [Fact]
    public void Live_records_carry_their_provenance()
    {
        var result = MapSample();

        Assert.Equal(CalendarSource.LiveUpstream, result.Source);
        Assert.All(result.Events, e =>
        {
            Assert.Equal(CalendarSource.LiveUpstream, e.Source);
            Assert.True(e.IsVerified);
            Assert.Equal(FetchedAt.UtcDateTime, e.SyncedAtUtc);
            Assert.Equal(2026, e.Season);
        });
    }

    [Fact]
    public void Every_mapped_record_converts_to_a_valid_domain_event()
    {
        var result = MapSample();

        foreach (var record in result.Events)
        {
            var domain = record.ToDomain();
            Assert.Equal(record.Round, domain.Round);
            Assert.NotNull(domain.RaceSession);
        }
    }

    [Fact]
    public void An_empty_response_maps_to_no_events_rather_than_failing()
    {
        var result = ErgastCalendarMapper.Map(2026, new ErgastResponse(), IngestionDefaults.Standard, FetchedAt, "test");

        Assert.Empty(result.Events);
        Assert.Empty(result.Warnings);
    }
}
