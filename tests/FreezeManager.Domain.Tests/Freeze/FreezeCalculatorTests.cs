using FreezeManager.Domain.Calendar;
using FreezeManager.Domain.Freeze;
using FreezeManager.Domain.Services;
using FreezeManager.Domain.Tests.Fixtures;
using Xunit;

namespace FreezeManager.Domain.Tests.Freeze;

/// <summary>
/// Every expectation is written as an offset from the FP1 start of the reference weekend (T).
/// Under the default policies that gives:
///   Trackside    T-48h .. T+55h   (48h before FP1, to 2h after parc ferme release)
///   Race support T-24h .. T+53h   (24h before FP1, to 3h after the race)
///   Corporate    each session +/- 30 minutes, advisory only
/// </summary>
public class FreezeCalculatorTests
{
    private static readonly DateTimeOffset T = Weekends.ReferenceFp1;

    private static FreezeCalculator SingleWeekend() =>
        new(Weekends.SingleConventionalWeekend());

    // ---------------------------------------------------------------- tier boundaries

    [Fact]
    public void Trackside_freezes_48_hours_before_the_first_session_because_the_kit_ships_before_anyone_drives()
    {
        var calculator = SingleWeekend();

        Assert.True(calculator.Evaluate(ServiceTier.Trackside, T.AddHours(-49)).IsOpen);
        Assert.True(calculator.Evaluate(ServiceTier.Trackside, T.AddHours(-48)).IsFrozen);
        Assert.True(calculator.Evaluate(ServiceTier.Trackside, T.AddHours(-47)).IsFrozen);
    }

    [Fact]
    public void Trackside_thaws_two_hours_after_parc_ferme_release_to_cover_tear_down()
    {
        var calculator = SingleWeekend();

        Assert.True(calculator.Evaluate(ServiceTier.Trackside, T.AddHours(54)).IsFrozen);
        Assert.True(calculator.Evaluate(ServiceTier.Trackside, T.AddHours(55)).IsOpen);
    }

    [Fact]
    public void Race_support_thaws_before_trackside_does()
    {
        var calculator = SingleWeekend();
        var at = T.AddHours(54);

        Assert.True(calculator.Evaluate(ServiceTier.RaceSupport, at).IsOpen);
        Assert.True(calculator.Evaluate(ServiceTier.Trackside, at).IsFrozen);
    }

    [Fact]
    public void Race_support_freezes_a_day_before_the_first_session_not_two()
    {
        var calculator = SingleWeekend();

        Assert.True(calculator.Evaluate(ServiceTier.RaceSupport, T.AddHours(-25)).IsOpen);
        Assert.True(calculator.Evaluate(ServiceTier.RaceSupport, T.AddHours(-24)).IsFrozen);
    }

    // ---------------------------------------------------------------- advisory tier

    [Fact]
    public void Corporate_warns_during_a_session_but_never_blocks()
    {
        var calculator = SingleWeekend();
        var duringTheRace = T.AddHours(49);

        var evaluation = calculator.Evaluate(ServiceTier.Corporate, duringTheRace);

        Assert.True(evaluation.IsAdvisory);
        Assert.False(evaluation.IsFrozen);

        var assessment = calculator.AssessChangeWindow(
            ServiceTier.Corporate, duringTheRace, duringTheRace.AddMinutes(30));

        Assert.Equal(ChangeWindowVerdict.AllowedWithWarning, assessment.Verdict);
        Assert.False(assessment.IsBlocked);
    }

    [Fact]
    public void Corporate_stays_deployable_in_the_gap_between_practice_sessions()
    {
        // FP1 ends T+1h and FP2 opens its advisory window at T+3.5h.
        var calculator = SingleWeekend();

        Assert.True(calculator.Evaluate(ServiceTier.Corporate, T.AddHours(2)).IsOpen);
    }

    // ---------------------------------------------------------------- the change gate

    [Fact]
    public void A_change_that_spans_a_freeze_is_blocked_rather_than_trimmed_to_fit()
    {
        var calculator = SingleWeekend();

        // Starts well before the freeze and ends well after it: the whole weekend is inside it.
        var assessment = calculator.AssessChangeWindow(
            ServiceTier.Trackside, T.AddHours(-72), T.AddHours(72));

        Assert.Equal(ChangeWindowVerdict.BlockedByFreeze, assessment.Verdict);
        Assert.NotEmpty(assessment.Conflicts);
    }

    [Fact]
    public void A_change_that_finishes_exactly_as_the_freeze_begins_is_allowed()
    {
        var calculator = SingleWeekend();

        var assessment = calculator.AssessChangeWindow(
            ServiceTier.Trackside, T.AddHours(-52), T.AddHours(-48));

        Assert.Equal(ChangeWindowVerdict.Allowed, assessment.Verdict);
        Assert.Empty(assessment.Conflicts);
    }

    [Fact]
    public void A_blocked_change_is_handed_the_next_window_that_actually_fits_it()
    {
        var calculator = SingleWeekend();

        var assessment = calculator.AssessChangeWindow(
            ServiceTier.Trackside, T, T.AddHours(4));

        Assert.True(assessment.IsBlocked);
        Assert.NotNull(assessment.SuggestedAlternative);
        Assert.Equal(T.AddHours(55), assessment.SuggestedAlternative!.StartUtc);
        Assert.True(assessment.SuggestedAlternative.Duration >= assessment.RequestedDuration);
    }

    // ---------------------------------------------------------------- calendar congestion

    [Fact]
    public void Back_to_back_races_leave_a_65_hour_window_and_nothing_more()
    {
        var calculator = new FreezeCalculator(Weekends.ConsecutiveWeekends(2));

        // Round 1 thaws at T+55h; round 2 freezes at (T+168h)-48h = T+120h.
        var open = calculator.NextOpenWindow(ServiceTier.Trackside, T, TimeSpan.FromHours(48));

        Assert.NotNull(open);
        Assert.Equal(T.AddHours(55), open!.StartUtc);
        Assert.Equal(T.AddHours(120), open.EndUtc);
        Assert.Equal(TimeSpan.FromHours(65), open.Duration);
    }

    [Fact]
    public void A_change_too_long_for_the_gap_between_back_to_backs_is_pushed_past_both()
    {
        var calculator = new FreezeCalculator(Weekends.ConsecutiveWeekends(2));

        var open = calculator.NextOpenWindow(ServiceTier.Trackside, T, TimeSpan.FromHours(72));

        Assert.NotNull(open);
        Assert.Equal(T.AddHours(223), open!.StartUtc);
    }

    [Fact]
    public void A_triple_header_has_no_window_long_enough_for_a_major_change()
    {
        var calculator = new FreezeCalculator(Weekends.ConsecutiveWeekends(3));

        // Two 65-hour gaps inside the triple header, so a four-day change waits it out entirely.
        var open = calculator.NextOpenWindow(ServiceTier.Trackside, T, TimeSpan.FromHours(96));

        Assert.NotNull(open);
        Assert.Equal(T.AddHours(391), open!.StartUtc);
    }

    [Fact]
    public void A_triple_header_still_reports_three_separate_windows_not_one()
    {
        var calculator = new FreezeCalculator(Weekends.ConsecutiveWeekends(3));

        var windows = calculator
            .WindowsFor(ServiceTier.Trackside)
            .Where(w => !w.IsAdvisory)
            .ToArray();

        Assert.Equal(3, windows.Length);
    }

    [Fact]
    public void A_service_with_a_long_load_in_lead_sees_back_to_backs_merge_into_one_freeze()
    {
        // Some trackside freight ships a week ahead. At a 120-hour lead the gap disappears entirely
        // and the honest answer is a single continuous freeze, not two windows with a fictional gap.
        var policies = new FreezePolicySet(new[]
        {
            new FreezePolicy(
                ServiceTier.Trackside,
                "Sea freight",
                new FreezeRule[]
                {
                    new EventAnchoredRule(
                        FreezeAnchor.FirstSessionStart,
                        TimeSpan.FromHours(-120),
                        FreezeAnchor.ParcFermeRelease,
                        TimeSpan.FromHours(2),
                        "Sea freight freeze",
                        endFallbackAnchor: FreezeAnchor.LastSessionEnd)
                })
        });

        var calculator = new FreezeCalculator(Weekends.ConsecutiveWeekends(2), policies);
        var windows = calculator.WindowsFor(ServiceTier.Trackside);

        var only = Assert.Single(windows);
        Assert.Equal(T.AddHours(-120), only.StartUtc);
        Assert.Equal(T.AddHours(223), only.EndUtc);
        Assert.Equal(new[] { 1, 2 }, only.Rounds);
    }

    // ---------------------------------------------------------------- weekend shapes

    [Fact]
    public void A_sprint_weekend_opens_no_deployment_gap_between_its_two_parc_ferme_windows()
    {
        // Parc ferme lifts between the sprint (ends T+23h) and qualifying (starts T+26h). That is a
        // gap for the race engineers, not for IT: the trackside freeze spans the whole weekend.
        var calculator = new FreezeCalculator(Weekends.Calendar(Weekends.Sprint(1, T)));

        Assert.True(calculator.Evaluate(ServiceTier.Trackside, T.AddHours(24)).IsFrozen);
        Assert.Single(calculator.WindowsFor(ServiceTier.Trackside).Where(w => !w.IsAdvisory));
    }

    [Fact]
    public void An_event_with_no_published_parc_ferme_times_falls_back_to_the_last_session_end()
    {
        // Parc ferme times are often published late. Falling back fails safe -- it does not quietly
        // drop the freeze because one field was missing.
        var calculator = new FreezeCalculator(
            Weekends.Calendar(Weekends.Conventional(1, T, withParcFerme: false)));

        Assert.True(calculator.Evaluate(ServiceTier.Trackside, T.AddHours(51)).IsFrozen);
        Assert.True(calculator.Evaluate(ServiceTier.Trackside, T.AddHours(52)).IsOpen);
    }

    [Fact]
    public void A_cancelled_event_freezes_nothing()
    {
        var calculator = new FreezeCalculator(
            Weekends.Calendar(Weekends.Conventional(1, T, status: EventStatus.Cancelled)));

        Assert.Empty(calculator.WindowsFor(ServiceTier.Trackside));
        Assert.True(calculator.Evaluate(ServiceTier.Trackside, T).IsOpen);
    }

    [Fact]
    public void A_session_delayed_by_a_red_flag_drags_the_freeze_out_with_it()
    {
        var onTime = SingleWeekend();

        var delayed = new FreezeCalculator(Weekends.Calendar(new RaceEvent(
            1, "Round 1 Grand Prix", "Testville", "Testland", "Europe/London", EventFormat.Conventional,
            new[]
            {
                new Session(SessionType.Practice1, T, T.AddHours(1)),
                new Session(SessionType.Qualifying, T.AddHours(25), T.AddHours(26)),
                new Session(
                    SessionType.Race,
                    T.AddHours(48),
                    T.AddHours(50),
                    actualStartUtc: T.AddHours(48).AddMinutes(45))
            },
            new[] { new ParcFermeWindow(T.AddHours(25), T.AddHours(53)) })));

        // Race support runs to race end + 3h. On the timetable that is T+53h; delayed 45 minutes it
        // is T+53h45m, and the factory stays frozen through the gap.
        Assert.True(onTime.Evaluate(ServiceTier.RaceSupport, T.AddHours(53).AddMinutes(30)).IsOpen);
        Assert.True(delayed.Evaluate(ServiceTier.RaceSupport, T.AddHours(53).AddMinutes(30)).IsFrozen);
    }

    // ---------------------------------------------------------------- time zones

    [Fact]
    public void Windows_are_anchored_to_instants_so_a_circuit_local_offset_cannot_shift_them()
    {
        // The same weekend, described in UTC, in US central daylight time, and in Japan standard
        // time. Identical instants, so identical windows -- which is what stops the week each year
        // when the US and Europe have not both changed their clocks from moving a freeze.
        var inUtc = new FreezeCalculator(Weekends.Calendar(Weekends.Conventional(1, T)));

        var inCentral = new FreezeCalculator(Weekends.Calendar(
            Weekends.Conventional(1, T.ToOffset(TimeSpan.FromHours(-5)))));

        var inJapan = new FreezeCalculator(Weekends.Calendar(
            Weekends.Conventional(1, T.ToOffset(TimeSpan.FromHours(9)))));

        var utcWindows = inUtc.WindowsFor(ServiceTier.Trackside);
        var centralWindows = inCentral.WindowsFor(ServiceTier.Trackside);
        var japanWindows = inJapan.WindowsFor(ServiceTier.Trackside);

        Assert.Equal(utcWindows.Count, centralWindows.Count);
        Assert.Equal(utcWindows.Count, japanWindows.Count);

        for (var i = 0; i < utcWindows.Count; i++)
        {
            Assert.Equal(utcWindows[i].StartUtc, centralWindows[i].StartUtc);
            Assert.Equal(utcWindows[i].EndUtc, centralWindows[i].EndUtc);
            Assert.Equal(utcWindows[i].StartUtc, japanWindows[i].StartUtc);
            Assert.Equal(utcWindows[i].EndUtc, japanWindows[i].EndUtc);
        }
    }

    [Fact]
    public void A_saturday_night_race_in_las_vegas_keeps_the_factory_frozen_into_sunday()
    {
        // Las Vegas runs the race on Saturday evening local time, which is Sunday morning at a
        // European factory. Anyone reasoning in local days schedules straight into a live freeze.
        var pacific = TimeSpan.FromHours(-8);
        var fp1 = new DateTimeOffset(2026, 11, 19, 18, 30, 0, pacific);
        var raceStart = new DateTimeOffset(2026, 11, 21, 20, 0, 0, pacific);

        var vegas = new RaceEvent(
            22, "Las Vegas Grand Prix", "Las Vegas Strip Circuit", "United States",
            "America/Los_Angeles", EventFormat.Conventional,
            new[]
            {
                new Session(SessionType.Practice1, fp1, fp1.AddHours(1)),
                new Session(SessionType.Qualifying, raceStart.AddDays(-1), raceStart.AddDays(-1).AddHours(1)),
                new Session(SessionType.Race, raceStart, raceStart.AddHours(2))
            },
            new[] { new ParcFermeWindow(raceStart.AddDays(-1), raceStart.AddHours(5)) });

        var calculator = new FreezeCalculator(Weekends.Calendar(vegas));

        // Saturday 20:00 Pacific is Sunday 04:00 UTC.
        Assert.Equal(DayOfWeek.Saturday, raceStart.DateTime.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, raceStart.UtcDateTime.DayOfWeek);

        // Race ends Sunday 06:00 UTC; race support runs three hours past that.
        var sundayMorningAtTheFactory = new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
        Assert.True(calculator.Evaluate(ServiceTier.RaceSupport, sundayMorningAtTheFactory).IsFrozen);
        Assert.True(calculator.Evaluate(
            ServiceTier.RaceSupport,
            new DateTimeOffset(2026, 11, 22, 9, 30, 0, TimeSpan.Zero)).IsOpen);
    }

    // ---------------------------------------------------------------- reporting

    [Fact]
    public void Evaluation_reports_how_long_is_left_on_the_freeze()
    {
        var evaluation = SingleWeekend().Evaluate(ServiceTier.Trackside, T);

        Assert.True(evaluation.IsFrozen);
        Assert.Equal(TimeSpan.FromHours(55), evaluation.TimeUntilThaw);
        Assert.Null(evaluation.TimeUntilNextFreeze);
        Assert.Equal("Trackside critical", evaluation.PolicyName);
    }

    [Fact]
    public void Evaluation_reports_how_long_until_the_next_freeze_when_currently_open()
    {
        var evaluation = SingleWeekend().Evaluate(ServiceTier.Trackside, T.AddHours(-72));

        Assert.True(evaluation.IsOpen);
        Assert.Null(evaluation.TimeUntilThaw);
        Assert.Equal(TimeSpan.FromHours(24), evaluation.TimeUntilNextFreeze);
    }

    [Fact]
    public void The_reason_shown_to_a_blocked_engineer_names_the_round_and_the_circuit()
    {
        var evaluation = SingleWeekend().Evaluate(ServiceTier.Trackside, T);

        Assert.NotNull(evaluation.Reason);
        Assert.Contains("Round 1", evaluation.Reason!);
        Assert.Contains("Testville", evaluation.Reason!);
    }

    [Fact]
    public void No_open_window_is_offered_when_none_fits_inside_the_horizon()
    {
        var calculator = SingleWeekend();

        var open = calculator.NextOpenWindow(
            ServiceTier.Trackside, T, TimeSpan.FromHours(48), horizon: TimeSpan.FromHours(24));

        Assert.Null(open);
    }

    [Fact]
    public void A_change_needs_a_positive_duration()
    {
        var calculator = SingleWeekend();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            calculator.NextOpenWindow(ServiceTier.Trackside, T, TimeSpan.Zero));

        Assert.Throws<ArgumentException>(() =>
            calculator.AssessChangeWindow(ServiceTier.Trackside, T, T));
    }
}
