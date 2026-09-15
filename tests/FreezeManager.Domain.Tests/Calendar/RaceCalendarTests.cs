using FreezeManager.Domain.Calendar;
using FreezeManager.Domain.Tests.Fixtures;
using Xunit;

namespace FreezeManager.Domain.Tests.Calendar;

public class RaceCalendarTests
{
    private static readonly DateTimeOffset T = Weekends.ReferenceFp1;

    [Fact]
    public void Events_are_held_in_round_order()
    {
        var calendar = Weekends.Calendar(
            Weekends.Conventional(3, T.AddDays(14)),
            Weekends.Conventional(1, T),
            Weekends.Conventional(2, T.AddDays(7)));

        Assert.Equal(new[] { 1, 2, 3 }, calendar.Events.Select(e => e.Round));
    }

    [Fact]
    public void A_round_cannot_appear_twice()
    {
        Assert.Throws<ArgumentException>(() => Weekends.Calendar(
            Weekends.Conventional(1, T),
            Weekends.Conventional(1, T.AddDays(7))));
    }

    [Fact]
    public void Cancelled_events_are_kept_for_history_but_excluded_from_active_events()
    {
        // Historic change records still have to resolve the round they referenced.
        var calendar = Weekends.Calendar(
            Weekends.Conventional(1, T),
            Weekends.Conventional(2, T.AddDays(7), status: EventStatus.Cancelled));

        Assert.Equal(2, calendar.Events.Count);
        Assert.Single(calendar.ActiveEvents);
        Assert.NotNull(calendar.EventByRound(2));
    }

    [Fact]
    public void The_next_event_skips_anything_cancelled()
    {
        var calendar = Weekends.Calendar(
            Weekends.Conventional(1, T),
            Weekends.Conventional(2, T.AddDays(7), status: EventStatus.Cancelled),
            Weekends.Conventional(3, T.AddDays(14)));

        var next = calendar.NextEventAfter(T.AddDays(1));

        Assert.NotNull(next);
        Assert.Equal(3, next!.Round);
    }

    [Fact]
    public void There_is_no_next_event_once_the_season_is_done()
    {
        var calendar = Weekends.SingleConventionalWeekend();

        Assert.Null(calendar.NextEventAfter(T.AddDays(30)));
    }
}
