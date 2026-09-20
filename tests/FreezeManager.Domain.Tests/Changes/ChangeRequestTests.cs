using FreezeManager.Domain.Changes;
using Xunit;

namespace FreezeManager.Domain.Tests.Changes;

public class ChangeRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowStart = new(2026, 5, 1, 20, 0, 0, TimeSpan.Zero);

    private static ChangeRequest Draft(
        string? description = "Patch the ingest nodes.",
        string? implementation = "Rolling restart, one node at a time.",
        string? backout = "Restore the previous image.",
        params string[] services)
    {
        return new ChangeRequest(
            ChangeReference.Create(2026, 1),
            "Patch telemetry ingest",
            "s.sindhe",
            ChangeType.Normal,
            ChangeImpact.Medium,
            ChangeLikelihood.Low,
            services.Length == 0 ? new[] { "telemetry-ingest" } : services,
            WindowStart,
            WindowStart.AddHours(4),
            Now,
            description,
            implementation,
            backout);
    }

    [Fact]
    public void A_new_change_starts_as_a_draft_and_is_editable()
    {
        var change = Draft();

        Assert.Equal(ChangeState.Draft, change.State);
        Assert.True(change.IsEditable);
        Assert.False(change.IsTerminal);
        Assert.Equal("CHG-2026-0001", change.Reference.ToString());
    }

    [Fact]
    public void A_change_window_must_end_after_it_starts()
    {
        Assert.Throws<ArgumentException>(() => new ChangeRequest(
            ChangeReference.Create(2026, 1), "t", "u", ChangeType.Normal,
            ChangeImpact.Low, ChangeLikelihood.Low, new[] { "svc" },
            WindowStart, WindowStart, Now));
    }

    [Fact]
    public void Affected_services_are_deduplicated_and_ordered()
    {
        var change = Draft(services: ["mission-control", "telemetry-ingest", "Telemetry-Ingest"]);

        Assert.Equal(new[] { "mission-control", "telemetry-ingest" }, change.AffectedServiceKeys);
    }

    [Fact]
    public void Risk_is_derived_from_impact_and_likelihood_so_two_people_cannot_disagree()
    {
        Assert.Equal(RiskLevel.Low, ChangeRiskMatrix.Evaluate(ChangeImpact.Low, ChangeLikelihood.Low));
        Assert.Equal(RiskLevel.Medium, ChangeRiskMatrix.Evaluate(ChangeImpact.High, ChangeLikelihood.Low));
        Assert.Equal(RiskLevel.High, ChangeRiskMatrix.Evaluate(ChangeImpact.Medium, ChangeLikelihood.High));
        Assert.Equal(RiskLevel.Critical, ChangeRiskMatrix.Evaluate(ChangeImpact.High, ChangeLikelihood.High));
    }

    [Fact]
    public void A_draft_may_be_incomplete_but_a_submission_may_not()
    {
        var incomplete = Draft(description: null, implementation: null, backout: null);

        // Being a draft is fine.
        Assert.Equal(ChangeState.Draft, incomplete.State);

        var exception = Assert.Throws<ChangeValidationException>(() =>
            incomplete.TransitionTo(ChangeState.Submitted, Now));

        Assert.Contains(exception.Problems, p => p.Contains("description", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(exception.Problems, p => p.Contains("implementation plan", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(exception.Problems, p => p.Contains("backout plan", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ChangeState.Draft, incomplete.State);
    }

    [Fact]
    public void A_submission_without_an_affected_service_is_refused()
    {
        var change = new ChangeRequest(
            ChangeReference.Create(2026, 2), "t", "u", ChangeType.Normal,
            ChangeImpact.Low, ChangeLikelihood.Low, Array.Empty<string>(),
            WindowStart, WindowStart.AddHours(1), Now,
            "d", "i", "b");

        Assert.Throws<ChangeValidationException>(() => change.TransitionTo(ChangeState.Submitted, Now));
    }

    [Fact]
    public void A_complete_draft_submits_cleanly()
    {
        var change = Draft();
        change.TransitionTo(ChangeState.Submitted, Now.AddMinutes(5));

        Assert.Equal(ChangeState.Submitted, change.State);
        Assert.Equal(Now.AddMinutes(5), change.UpdatedAtUtc);
        Assert.False(change.IsEditable);
    }

    [Fact]
    public void An_illegal_transition_throws_and_leaves_the_state_alone()
    {
        var change = Draft();

        Assert.Throws<InvalidChangeTransitionException>(() =>
            change.TransitionTo(ChangeState.Implementing, Now));

        Assert.Equal(ChangeState.Draft, change.State);
    }

    [Fact]
    public void Only_a_draft_can_be_edited()
    {
        var change = Draft();
        change.TransitionTo(ChangeState.Submitted, Now);

        Assert.Throws<ChangeValidationException>(() => change.UpdateDetails(
            "new title", "d", "i", "b", ChangeType.Normal,
            ChangeImpact.Low, ChangeLikelihood.Low, new[] { "telemetry-ingest" }, Now));
    }

    [Fact]
    public void A_withdrawn_change_becomes_editable_again()
    {
        var change = Draft();
        change.TransitionTo(ChangeState.Submitted, Now);
        change.TransitionTo(ChangeState.Draft, Now.AddMinutes(1));

        Assert.True(change.IsEditable);
        change.UpdateDetails("revised", "d", "i", "b", ChangeType.Normal,
            ChangeImpact.Low, ChangeLikelihood.Low, new[] { "telemetry-ingest" }, Now.AddMinutes(2));

        Assert.Equal("revised", change.Title);
    }

    [Fact]
    public void A_change_being_implemented_cannot_be_rescheduled_because_that_is_a_new_change()
    {
        var change = Draft();
        change.TransitionTo(ChangeState.Submitted, Now);
        change.TransitionTo(ChangeState.Approved, Now);
        change.TransitionTo(ChangeState.Scheduled, Now);
        change.TransitionTo(ChangeState.Implementing, Now);

        Assert.Throws<ChangeValidationException>(() =>
            change.Reschedule(WindowStart.AddDays(1), WindowStart.AddDays(1).AddHours(4), Now));
    }

    [Fact]
    public void An_approved_change_can_still_have_its_window_confirmed()
    {
        // Confirming a window is what scheduling does, so approval must not freeze the window in
        // place. Approval agrees to the work; the freeze gate re-checks the slot.
        var change = Draft();
        change.TransitionTo(ChangeState.Submitted, Now);
        change.TransitionTo(ChangeState.Approved, Now);

        change.Reschedule(WindowStart.AddDays(3), WindowStart.AddDays(3).AddHours(4), Now);

        Assert.Equal(WindowStart.AddDays(3), change.RequestedStartUtc);
        Assert.Equal(ChangeState.Approved, change.State);
    }

    [Fact]
    public void Rescheduling_keeps_the_requested_duration_the_callers_business()
    {
        var change = Draft();
        change.Reschedule(WindowStart.AddDays(7), WindowStart.AddDays(7).AddHours(2), Now);

        Assert.Equal(TimeSpan.FromHours(2), change.RequestedDuration);
    }

    [Fact]
    public void The_full_happy_path_runs_to_closed()
    {
        var change = Draft();

        foreach (var state in new[]
                 {
                     ChangeState.Submitted, ChangeState.Approved, ChangeState.Scheduled, ChangeState.Implementing,
                     ChangeState.Implemented, ChangeState.Closed
                 })
        {
            change.TransitionTo(state, Now);
        }

        Assert.Equal(ChangeState.Closed, change.State);
        Assert.True(change.IsTerminal);
        Assert.Empty(change.NextStates);
    }

    [Fact]
    public void A_failed_change_can_be_backed_out_and_closed()
    {
        var change = Draft();
        change.TransitionTo(ChangeState.Submitted, Now);
        change.TransitionTo(ChangeState.Approved, Now);
        change.TransitionTo(ChangeState.Scheduled, Now);
        change.TransitionTo(ChangeState.Implementing, Now);
        change.TransitionTo(ChangeState.Failed, Now);
        change.TransitionTo(ChangeState.RolledBack, Now);
        change.TransitionTo(ChangeState.Closed, Now);

        Assert.Equal(ChangeState.Closed, change.State);
    }

    [Theory]
    [InlineData("CHG-2026-0001", 2026, 1)]
    [InlineData("CHG-2026-0042", 2026, 42)]
    [InlineData("chg-2026-0007", 2026, 7)]
    public void A_reference_round_trips(string text, int year, int sequence)
    {
        Assert.True(ChangeReference.TryParse(text, out var reference));
        Assert.Equal(year, reference.Year);
        Assert.Equal(sequence, reference.Sequence);
        Assert.Equal(ChangeReference.Create(year, sequence).ToString(), reference.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("CHG-2026")]
    [InlineData("INC-2026-0001")]
    [InlineData("CHG-2026-0000")]
    [InlineData("CHG-abcd-0001")]
    public void A_malformed_reference_is_rejected(string text)
    {
        Assert.False(ChangeReference.TryParse(text, out _));
    }
}
