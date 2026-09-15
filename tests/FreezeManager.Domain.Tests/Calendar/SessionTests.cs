using FreezeManager.Domain.Calendar;
using Xunit;

namespace FreezeManager.Domain.Tests.Calendar;

public class SessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_session_must_end_after_it_starts()
    {
        Assert.Throws<ArgumentException>(() =>
            new Session(SessionType.Practice1, Start, Start));
    }

    [Fact]
    public void Scheduled_times_are_used_until_the_session_actually_runs()
    {
        var session = new Session(SessionType.Practice1, Start, Start.AddHours(1));

        Assert.Equal(Start, session.EffectiveStartUtc);
        Assert.Equal(Start.AddHours(1), session.EffectiveEndUtc);
        Assert.False(session.HasStarted);
        Assert.False(session.IsDelayed);
    }

    [Fact]
    public void A_session_that_starts_late_is_projected_to_end_late_by_the_same_margin()
    {
        // No actual end yet: the freeze must not lift on the published timetable while the session
        // is still running.
        var session = new Session(
            SessionType.Race,
            Start,
            Start.AddHours(2),
            actualStartUtc: Start.AddMinutes(45));

        Assert.True(session.IsDelayed);
        Assert.Equal(Start.AddMinutes(45), session.EffectiveStartUtc);
        Assert.Equal(Start.AddMinutes(45).AddHours(2), session.EffectiveEndUtc);
    }

    [Fact]
    public void A_known_actual_end_wins_over_the_projection()
    {
        var session = new Session(
            SessionType.Race,
            Start,
            Start.AddHours(2),
            actualStartUtc: Start.AddMinutes(45),
            actualEndUtc: Start.AddHours(4));

        Assert.Equal(Start.AddHours(4), session.EffectiveEndUtc);
        Assert.True(session.HasFinished);
    }

    [Fact]
    public void Actual_times_must_be_ordered_too()
    {
        Assert.Throws<ArgumentException>(() => new Session(
            SessionType.Race,
            Start,
            Start.AddHours(2),
            actualStartUtc: Start.AddHours(1),
            actualEndUtc: Start.AddMinutes(30)));
    }

    [Fact]
    public void Times_are_normalised_to_utc_whatever_offset_they_arrive_in()
    {
        var singaporeEvening = new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.FromHours(8));
        var session = new Session(SessionType.Race, singaporeEvening, singaporeEvening.AddHours(2));

        Assert.Equal(TimeSpan.Zero, session.ScheduledStartUtc.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero), session.ScheduledStartUtc);
    }
}
