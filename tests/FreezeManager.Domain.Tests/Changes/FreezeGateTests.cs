using FreezeManager.Domain.Changes;
using FreezeManager.Domain.Freeze;
using FreezeManager.Domain.Services;
using FreezeManager.Domain.Tests.Fixtures;
using Xunit;

namespace FreezeManager.Domain.Tests.Changes;

/// <summary>
/// The reference weekend again: FP1 at T. Trackside is frozen T-48h .. T+55h, race support
/// T-24h .. T+53h, corporate advisory around each session.
/// </summary>
public class FreezeGateTests
{
    private static readonly DateTimeOffset T = Weekends.ReferenceFp1;

    private static readonly Dictionary<string, ServiceTier> Catalogue = new(StringComparer.OrdinalIgnoreCase)
    {
        ["telemetry-ingest"] = ServiceTier.Trackside,
        ["mission-control"] = ServiceTier.RaceSupport,
        ["intranet"] = ServiceTier.Corporate
    };

    private static FreezeGate Gate(int weekends = 1) =>
        new(new FreezeCalculator(Weekends.ConsecutiveWeekends(weekends)));

    private static ChangeRequest Change(
        DateTimeOffset start,
        TimeSpan duration,
        params string[] services)
    {
        return new ChangeRequest(
            ChangeReference.Create(2026, 1),
            "Patch",
            "s.sindhe",
            ChangeType.Normal,
            ChangeImpact.Medium,
            ChangeLikelihood.Low,
            services,
            start,
            start + duration,
            T.AddDays(-30),
            "d", "i", "b");
    }

    [Fact]
    public void A_change_outside_every_window_is_allowed()
    {
        var decision = Gate().Evaluate(Change(T.AddHours(-96), TimeSpan.FromHours(4), "telemetry-ingest"), Catalogue);

        Assert.Equal(FreezeGateOutcome.Allowed, decision.Outcome);
        Assert.True(decision.IsAllowed);
        Assert.Empty(decision.Conflicts);
        Assert.Null(decision.SuggestedOpenWindow);
    }

    [Fact]
    public void A_change_inside_the_freeze_is_blocked_and_told_when_it_can_run()
    {
        var decision = Gate().Evaluate(Change(T, TimeSpan.FromHours(4), "telemetry-ingest"), Catalogue);

        Assert.Equal(FreezeGateOutcome.Blocked, decision.Outcome);
        Assert.False(decision.IsAllowed);
        Assert.NotEmpty(decision.Conflicts);

        // The refusal always carries the way forward.
        Assert.NotNull(decision.SuggestedOpenWindow);
        Assert.Equal(T.AddHours(55), decision.SuggestedOpenWindow!.StartUtc);
        Assert.True(decision.SuggestedOpenWindow.Duration >= TimeSpan.FromHours(4));
    }

    [Fact]
    public void The_suggested_window_is_one_the_change_would_actually_fit_in()
    {
        // Between back-to-backs there is a 65 hour gap. A 72 hour change does not fit, so the
        // suggestion must skip past both rounds rather than offering a gap that would fail again.
        var gate = Gate(weekends: 2);
        var change = Change(T, TimeSpan.FromHours(72), "telemetry-ingest");

        var decision = gate.Evaluate(change, Catalogue);

        Assert.Equal(FreezeGateOutcome.Blocked, decision.Outcome);
        Assert.Equal(T.AddHours(223), decision.SuggestedOpenWindow!.StartUtc);

        // Taking the suggestion actually works.
        var rescheduled = Change(decision.SuggestedOpenWindow.StartUtc, TimeSpan.FromHours(72), "telemetry-ingest");
        Assert.True(gate.Evaluate(rescheduled, Catalogue).IsAllowed);
    }

    [Fact]
    public void A_change_touching_only_an_advisory_tier_is_allowed_with_a_warning()
    {
        var duringTheRace = T.AddHours(49);
        var decision = Gate().Evaluate(Change(duringTheRace, TimeSpan.FromMinutes(30), "intranet"), Catalogue);

        Assert.Equal(FreezeGateOutcome.AllowedWithWarning, decision.Outcome);
        Assert.True(decision.IsAllowed);
        Assert.NotEmpty(decision.Conflicts);
        Assert.All(decision.Conflicts, c => Assert.True(c.IsAdvisory));
    }

    [Fact]
    public void A_change_spanning_several_tiers_is_judged_by_the_strictest_of_them()
    {
        // At T+54h race support has thawed but trackside has not. A change touching both is blocked.
        var at = T.AddHours(54);

        var raceSupportOnly = Gate().Evaluate(Change(at, TimeSpan.FromMinutes(30), "mission-control"), Catalogue);
        Assert.True(raceSupportOnly.IsAllowed);

        var both = Gate().Evaluate(
            Change(at, TimeSpan.FromMinutes(30), "mission-control", "telemetry-ingest"), Catalogue);

        Assert.Equal(FreezeGateOutcome.Blocked, both.Outcome);
        Assert.Equal(new[] { ServiceTier.Trackside, ServiceTier.RaceSupport }, both.Tiers);
    }

    [Fact]
    public void A_corporate_service_does_not_soften_a_trackside_freeze()
    {
        var decision = Gate().Evaluate(
            Change(T, TimeSpan.FromHours(2), "intranet", "telemetry-ingest"), Catalogue);

        Assert.Equal(FreezeGateOutcome.Blocked, decision.Outcome);
        Assert.All(decision.Conflicts, c => Assert.False(c.IsAdvisory));
    }

    [Fact]
    public void An_unknown_service_fails_closed_rather_than_being_treated_as_safe()
    {
        var decision = Gate().Evaluate(
            Change(T.AddHours(-96), TimeSpan.FromHours(1), "telemetry-ingest", "not-in-catalogue"), Catalogue);

        Assert.Equal(FreezeGateOutcome.UnknownService, decision.Outcome);
        Assert.False(decision.IsAllowed);
        Assert.Contains("not-in-catalogue", decision.UnknownServiceKeys);
    }

    [Fact]
    public void An_emergency_change_is_gated_exactly_like_any_other_in_this_phase()
    {
        var emergency = new ChangeRequest(
            ChangeReference.Create(2026, 9), "Emergency patch", "s.sindhe",
            ChangeType.Emergency, ChangeImpact.High, ChangeLikelihood.High,
            new[] { "telemetry-ingest" }, T, T.AddHours(1), T.AddDays(-1), "d", "i", "b");

        var decision = Gate().Evaluate(emergency, Catalogue);

        // The override that lets an emergency through arrives with the approval chain.
        Assert.Equal(FreezeGateOutcome.Blocked, decision.Outcome);
        Assert.NotNull(decision.SuggestedOpenWindow);
    }

    [Fact]
    public void A_change_that_merely_spans_a_freeze_is_blocked_not_trimmed()
    {
        var decision = Gate().Evaluate(
            Change(T.AddHours(-72), TimeSpan.FromHours(144), "telemetry-ingest"), Catalogue);

        Assert.Equal(FreezeGateOutcome.Blocked, decision.Outcome);
    }

    [Fact]
    public void The_gate_can_be_asked_for_the_next_window_without_being_asked_to_judge()
    {
        var change = Change(T, TimeSpan.FromHours(4), "telemetry-ingest");
        var window = Gate().NextOpenWindowFor(change, Catalogue, T);

        Assert.NotNull(window);
        Assert.Equal(T.AddHours(55), window!.StartUtc);
    }

    [Fact]
    public void A_blocked_change_with_no_window_inside_the_horizon_says_so_rather_than_inventing_one()
    {
        // A full season of back-to-backs with a change longer than any gap between them.
        var gate = new FreezeGate(new FreezeCalculator(Weekends.ConsecutiveWeekends(26)));
        var change = Change(T, TimeSpan.FromDays(30), "telemetry-ingest");

        var decision = gate.Evaluate(change, Catalogue);

        Assert.Equal(FreezeGateOutcome.Blocked, decision.Outcome);
        Assert.Null(decision.SuggestedOpenWindow);
        Assert.Contains("no window", decision.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
