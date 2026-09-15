using FreezeManager.Domain.Calendar;
using FreezeManager.Domain.Tests.Fixtures;
using Xunit;

namespace FreezeManager.Domain.Tests.Calendar;

public class RaceEventTests
{
    private static readonly DateTimeOffset T = Weekends.ReferenceFp1;

    [Fact]
    public void Sessions_are_held_in_chronological_order_whatever_order_they_arrive_in()
    {
        var raceEvent = new RaceEvent(
            1, "Test Grand Prix", "Testville", "Testland", "Europe/London", EventFormat.Conventional,
            new[]
            {
                new Session(SessionType.Race, T.AddHours(48), T.AddHours(50)),
                new Session(SessionType.Practice1, T, T.AddHours(1)),
                new Session(SessionType.Qualifying, T.AddHours(25), T.AddHours(26))
            });

        Assert.Equal(
            new[] { SessionType.Practice1, SessionType.Qualifying, SessionType.Race },
            raceEvent.Sessions.Select(s => s.Type));
    }

    [Fact]
    public void The_same_session_cannot_be_listed_twice()
    {
        var exception = Assert.Throws<ArgumentException>(() => new RaceEvent(
            1, "Test Grand Prix", "Testville", "Testland", "Europe/London", EventFormat.Conventional,
            new[]
            {
                new Session(SessionType.Practice1, T, T.AddHours(1)),
                new Session(SessionType.Practice1, T.AddHours(4), T.AddHours(5))
            }));

        Assert.Contains("Practice1", exception.Message);
    }

    [Fact]
    public void A_scheduled_event_must_have_at_least_one_session()
    {
        Assert.Throws<ArgumentException>(() => new RaceEvent(
            1, "Test Grand Prix", "Testville", "Testland", "Europe/London", EventFormat.Conventional,
            Array.Empty<Session>()));
    }

    [Fact]
    public void A_cancelled_event_may_have_no_sessions_at_all()
    {
        var raceEvent = new RaceEvent(
            1, "Test Grand Prix", "Testville", "Testland", "Europe/London", EventFormat.Conventional,
            Array.Empty<Session>(),
            status: EventStatus.Cancelled);

        Assert.True(raceEvent.IsCancelled);
        Assert.Null(raceEvent.FirstSessionStartUtc);
        Assert.Null(raceEvent.LastSessionEndUtc);
    }

    [Fact]
    public void Anchors_resolve_from_the_sessions_and_parc_ferme_windows_on_the_event()
    {
        var raceEvent = Weekends.Conventional(1, T);

        Assert.Equal(T, raceEvent.FirstSessionStartUtc);
        Assert.Equal(T.AddHours(50), raceEvent.LastSessionEndUtc);
        Assert.Equal(T.AddHours(48), raceEvent.RaceSession!.EffectiveStartUtc);
        Assert.Equal(T.AddHours(25), raceEvent.ParcFermeStartUtc);
        Assert.Equal(T.AddHours(53), raceEvent.ParcFermeReleaseUtc);
    }

    [Fact]
    public void A_sprint_weekend_releases_from_the_last_of_its_parc_ferme_windows()
    {
        var raceEvent = Weekends.Sprint(1, T);

        Assert.Equal(2, raceEvent.ParcFermeWindows.Count);
        Assert.Equal(T.AddHours(4), raceEvent.ParcFermeStartUtc);
        Assert.Equal(T.AddHours(53), raceEvent.ParcFermeReleaseUtc);
        Assert.True(raceEvent.IsSprintWeekend);
    }

    [Fact]
    public void Anchors_that_have_no_data_behind_them_resolve_to_null_rather_than_guessing()
    {
        var raceEvent = Weekends.Conventional(1, T, withParcFerme: false);

        Assert.Null(raceEvent.ParcFermeStartUtc);
        Assert.Null(raceEvent.ParcFermeReleaseUtc);
    }

    [Fact]
    public void The_circuit_time_zone_is_held_as_an_iana_identifier()
    {
        // A string, not a TimeZoneInfo: the domain stays serialisable and free of the Windows/IANA
        // identifier split, and resolving to a concrete zone stays a presentation concern.
        var raceEvent = Weekends.Conventional(1, T, timeZoneId: "America/Sao_Paulo");

        Assert.Equal("America/Sao_Paulo", raceEvent.LocalTimeZoneId);
    }
}
