using FreezeManager.Domain.Calendar;
using FreezeManager.Domain.Tests.Fixtures;
using Xunit;

namespace FreezeManager.Domain.Tests.Calendar;

public class CalendarCompletenessTests
{
    private static readonly DateTimeOffset T = Weekends.ReferenceFp1;

    [Fact]
    public void A_full_calendar_is_complete()
    {
        var report = CalendarCompleteness.Inspect(Weekends.ConsecutiveWeekends(5));

        Assert.True(report.IsComplete);
        Assert.Equal(5, report.RoundsPresent);
        Assert.Empty(report.MissingRounds);
    }

    [Fact]
    public void A_hole_in_the_middle_of_the_season_is_reported_as_an_unprotected_weekend()
    {
        // Rounds 1, 2 and 4: round 3 is a race weekend the engine believes is open.
        var calendar = Weekends.Calendar(
            Weekends.Conventional(1, T),
            Weekends.Conventional(2, T.AddDays(7)),
            Weekends.Conventional(4, T.AddDays(21)));

        var report = CalendarCompleteness.Inspect(calendar);

        Assert.False(report.IsComplete);
        Assert.True(report.HasUnprotectedWeekends);
        Assert.Equal(new[] { 3 }, report.MissingRounds);
        Assert.Contains("missing rounds 3", report.ToString());
    }

    [Fact]
    public void Rounds_missing_from_the_end_are_only_found_when_the_expected_count_is_given()
    {
        // Nothing in the data says a season has 24 rounds, so a truncated calendar looks complete
        // until someone says how long it should be.
        var calendar = Weekends.ConsecutiveWeekends(22);

        Assert.True(CalendarCompleteness.Inspect(calendar).IsComplete);

        var report = CalendarCompleteness.Inspect(calendar, expectedRounds: 24);

        Assert.False(report.IsComplete);
        Assert.Equal(new[] { 23, 24 }, report.MissingRounds);
    }

    [Fact]
    public void A_round_with_no_parc_ferme_is_incomplete_but_not_unprotected()
    {
        // The freeze still applies: decision 3 falls back to the last session end.
        var calendar = Weekends.Calendar(Weekends.Conventional(1, T, withParcFerme: false));

        var report = CalendarCompleteness.Inspect(calendar);

        Assert.False(report.IsComplete);
        Assert.False(report.HasUnprotectedWeekends);
        Assert.Contains(report.IncompleteRounds, g => g.Problem.Contains("parc ferme"));
    }

    [Fact]
    public void A_round_with_no_race_session_is_flagged()
    {
        var calendar = Weekends.Calendar(new RaceEvent(
            1, "Test Grand Prix", "Testville", "Testland", "Europe/London", EventFormat.Conventional,
            new[] { new Session(SessionType.Practice1, T, T.AddHours(1)) }));

        var report = CalendarCompleteness.Inspect(calendar);

        Assert.Contains(report.IncompleteRounds, g => g.Problem.Contains("No race session"));
    }

    [Fact]
    public void An_unresolved_time_zone_is_flagged_and_says_the_freeze_is_unaffected()
    {
        var calendar = Weekends.Calendar(
            Weekends.Conventional(1, T, timeZoneId: RaceEvent.UnresolvedTimeZoneId));

        var gap = Assert.Single(
            CalendarCompleteness.Inspect(calendar).IncompleteRounds,
            g => g.Problem.Contains("Time zone unresolved"));

        Assert.Contains("unaffected", gap.Problem);
        Assert.Equal("Testville", gap.Circuit);
    }

    [Fact]
    public void A_cancelled_round_is_a_deliberate_exclusion_not_a_hole()
    {
        // Cancelled events are excluded from the freeze on purpose and somebody recorded that.
        // A round nobody ever loaded is the thing worth shouting about.
        var calendar = Weekends.Calendar(
            Weekends.Conventional(1, T),
            Weekends.Conventional(2, T.AddDays(7), status: EventStatus.Cancelled),
            Weekends.Conventional(3, T.AddDays(14)));

        var report = CalendarCompleteness.Inspect(calendar);

        Assert.Empty(report.MissingRounds);
        Assert.False(report.HasUnprotectedWeekends);

        // And it is not inspected for missing detail either, because it will never run.
        Assert.DoesNotContain(report.IncompleteRounds, g => g.Round == 2);
    }

    [Fact]
    public void A_round_absent_from_the_calendar_is_still_a_hole_even_next_to_a_cancelled_one()
    {
        var calendar = Weekends.Calendar(
            Weekends.Conventional(1, T),
            Weekends.Conventional(2, T.AddDays(7), status: EventStatus.Cancelled),
            Weekends.Conventional(4, T.AddDays(21)));

        var report = CalendarCompleteness.Inspect(calendar);

        Assert.Equal(new[] { 3 }, report.MissingRounds);
        Assert.True(report.HasUnprotectedWeekends);
    }

    [Fact]
    public void An_empty_calendar_is_complete_only_in_the_sense_that_it_claims_nothing()
    {
        var report = CalendarCompleteness.Inspect(Weekends.Calendar());

        Assert.True(report.IsComplete);
        Assert.Equal(0, report.RoundsPresent);

        // Asked how long the season should be, it says everything is missing.
        Assert.Equal(24, CalendarCompleteness.Inspect(Weekends.Calendar(), 24).MissingRounds.Count);
    }
}
