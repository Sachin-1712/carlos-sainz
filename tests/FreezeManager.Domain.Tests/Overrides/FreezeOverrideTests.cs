using FreezeManager.Domain.Approvals;
using FreezeManager.Domain.Changes;
using FreezeManager.Domain.Overrides;
using FreezeManager.Domain.Services;
using Xunit;

namespace FreezeManager.Domain.Tests.Overrides;

public class FreezeOverrideTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 8, 14, 0, 0, TimeSpan.Zero);

    private const string GoodJustification =
        "Telemetry ingest is dropping packets mid-session and the standby has already failed over.";

    private static readonly ApprovalRequirement Trackside =
        ApprovalRequirement.For(new[] { ServiceTier.Trackside }, ChangeType.Emergency);

    private static FreezeOverride Request(bool breakGlass = false, TimeSpan? duration = null) =>
        new("CHG-2026-0001", "INC-1234", GoodJustification, "s.sindhe", Now, duration, breakGlass);

    private static ChangeApproval By(ApprovalRole role, ApprovalDecision decision = ApprovalDecision.Approved) =>
        new(role, role.ToString(), decision, Now);

    // ------------------------------------------------------------------ attributable

    [Fact]
    public void An_override_requires_a_linked_incident()
    {
        var exception = Assert.Throws<OverrideValidationException>(() =>
            new FreezeOverride("CHG-2026-0001", "", GoodJustification, "s.sindhe", Now));

        Assert.Contains(exception.Problems, p => p.Contains("incident", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("INC-1234", true)]
    [InlineData("inc-42", true)]
    [InlineData("CHG-1234", false)]
    [InlineData("INC", false)]
    [InlineData("INC-0", false)]
    [InlineData("INC-abc", false)]
    [InlineData("", false)]
    public void An_incident_reference_has_to_look_like_one(string value, bool expected)
    {
        Assert.Equal(expected, FreezeOverride.IsIncidentReference(value));
    }

    [Fact]
    public void An_override_requires_a_justification_long_enough_to_be_one()
    {
        var exception = Assert.Throws<OverrideValidationException>(() =>
            new FreezeOverride("CHG-2026-0001", "INC-1234", "urgent", "s.sindhe", Now));

        Assert.Contains(exception.Problems, p => p.Contains("justification", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_incident_reference_is_normalised()
    {
        Assert.Equal("INC-1234", new FreezeOverride("CHG-2026-0001", " inc-1234 ", GoodJustification, "s.sindhe", Now).IncidentReference);
    }

    // ------------------------------------------------------------------ time-boxed

    [Fact]
    public void A_grant_is_time_boxed_from_the_moment_it_is_granted()
    {
        var request = Request();

        request.RecordApproval(By(ApprovalRole.TracksideItLead), Trackside, Now);
        request.RecordApproval(By(ApprovalRole.HeadOfIt), Trackside, Now);
        request.RecordApproval(By(ApprovalRole.RaceEngineeringNominee), Trackside, Now.AddMinutes(10));

        Assert.Equal(OverrideState.Granted, request.State);
        Assert.Equal(Now.AddMinutes(10), request.GrantedAtUtc);
        Assert.Equal(Now.AddMinutes(10) + FreezeOverride.DefaultGrantDuration, request.ExpiresAtUtc);
    }

    [Fact]
    public void A_grant_cannot_be_asked_to_last_longer_than_an_emergency_plausibly_does()
    {
        Assert.Throws<OverrideValidationException>(() =>
            new FreezeOverride("CHG-2026-0001", "INC-1234", GoodJustification, "s.sindhe", Now,
                grantDuration: FreezeOverride.MaximumGrantDuration + TimeSpan.FromHours(1)));
    }

    [Fact]
    public void A_grant_is_in_force_up_to_but_not_including_its_expiry()
    {
        var request = Granted();

        Assert.True(request.IsInForceAt(Now));
        Assert.True(request.IsInForceAt(request.ExpiresAtUtc!.Value.AddSeconds(-1)));
        Assert.False(request.IsInForceAt(request.ExpiresAtUtc.Value));
    }

    [Fact]
    public void Expiry_happens_on_its_own_and_reports_that_it_was_this_call_that_did_it()
    {
        var request = Granted();
        var afterExpiry = request.ExpiresAtUtc!.Value.AddMinutes(1);

        Assert.False(request.ExpireIfElapsed(Now));
        Assert.True(request.ExpireIfElapsed(afterExpiry));
        Assert.Equal(OverrideState.Expired, request.State);

        // Idempotent: the sweeper writes one audit entry, not one per sweep.
        Assert.False(request.ExpireIfElapsed(afterExpiry));
    }

    [Fact]
    public void An_expired_override_cannot_be_used()
    {
        var request = Granted();
        request.ExpireIfElapsed(request.ExpiresAtUtc!.Value);

        Assert.Throws<OverrideValidationException>(() => request.MarkUsed(request.ExpiresAtUtc.Value));
    }

    [Fact]
    public void A_used_override_records_when_it_was_used()
    {
        var request = Granted();
        request.MarkUsed(Now.AddMinutes(5));

        Assert.Equal(OverrideState.Used, request.State);
        Assert.Equal(Now.AddMinutes(5), request.UsedAtUtc);
    }

    // ------------------------------------------------------------------ accountable

    [Fact]
    public void An_override_needs_the_whole_chain_its_tier_demands()
    {
        var request = Request();

        request.RecordApproval(By(ApprovalRole.TracksideItLead), Trackside, Now);
        Assert.Equal(OverrideState.Requested, request.State);

        request.RecordApproval(By(ApprovalRole.HeadOfIt), Trackside, Now);
        Assert.Equal(OverrideState.Requested, request.State);

        request.RecordApproval(By(ApprovalRole.RaceEngineeringNominee), Trackside, Now);
        Assert.Equal(OverrideState.Granted, request.State);
    }

    [Fact]
    public void One_rejection_stops_the_override_outright()
    {
        var request = Request();

        request.RecordApproval(By(ApprovalRole.TracksideItLead), Trackside, Now);
        request.RecordApproval(By(ApprovalRole.HeadOfIt, ApprovalDecision.Rejected), Trackside, Now);

        Assert.Equal(OverrideState.Rejected, request.State);
        Assert.Throws<OverrideValidationException>(() =>
            request.RecordApproval(By(ApprovalRole.RaceEngineeringNominee), Trackside, Now));
    }

    [Fact]
    public void Even_a_pre_approved_standard_change_needs_someone_to_approve_going_through_a_freeze()
    {
        // Going through the freeze is what is being approved, not the change.
        var preApproved = ApprovalRequirement.For(new[] { ServiceTier.Corporate }, ChangeType.Standard);
        var request = Request();

        Assert.Equal(new[] { ApprovalRole.HeadOfIt }, request.RequiredRoles(preApproved));
    }

    // ------------------------------------------------------------------ break-glass

    [Fact]
    public void Break_glass_is_granted_by_one_accountable_person()
    {
        var request = Request(breakGlass: true);

        request.RecordApproval(By(ApprovalRole.HeadOfIt), Trackside, Now);

        Assert.Equal(OverrideState.Granted, request.State);
    }

    [Fact]
    public void Break_glass_is_not_open_to_anyone_who_happens_to_be_awake()
    {
        var request = Request(breakGlass: true);

        Assert.Throws<OverrideValidationException>(() =>
            request.RecordApproval(By(ApprovalRole.RaceEngineeringNominee), Trackside, Now));
    }

    [Fact]
    public void Break_glass_costs_a_retrospective_within_24_hours()
    {
        var request = Request(breakGlass: true);
        request.RecordApproval(By(ApprovalRole.TracksideItLead), Trackside, Now);

        Assert.Equal(Now + FreezeOverride.RetrospectiveWindow, request.RetrospectiveDueAtUtc);
        Assert.True(request.RetrospectiveOutstanding);

        Assert.False(request.RetrospectiveOverdueAt(Now.AddHours(23)));
        Assert.True(request.RetrospectiveOverdueAt(Now.AddHours(24)));
    }

    [Fact]
    public void Completing_the_retrospective_clears_it_and_it_cannot_be_done_twice()
    {
        var request = Request(breakGlass: true);
        request.RecordApproval(By(ApprovalRole.HeadOfIt), Trackside, Now);

        var notes = "Root cause was an expired certificate on the standby ingest node; renewal automated.";
        request.CompleteRetrospective(notes, Now.AddHours(6));

        Assert.False(request.RetrospectiveOutstanding);
        Assert.False(request.RetrospectiveOverdueAt(Now.AddHours(48)));
        Assert.Equal(notes, request.RetrospectiveNotes);

        Assert.Throws<OverrideValidationException>(() => request.CompleteRetrospective(notes, Now.AddHours(7)));
    }

    [Fact]
    public void A_retrospective_needs_actual_notes()
    {
        var request = Request(breakGlass: true);
        request.RecordApproval(By(ApprovalRole.HeadOfIt), Trackside, Now);

        Assert.Throws<OverrideValidationException>(() => request.CompleteRetrospective("fixed", Now.AddHours(1)));
    }

    [Fact]
    public void A_normal_override_has_no_retrospective_to_complete()
    {
        var request = Granted();

        Assert.False(request.RetrospectiveOutstanding);
        Assert.Throws<OverrideValidationException>(() =>
            request.CompleteRetrospective(GoodJustification, Now.AddHours(1)));
    }

    [Fact]
    public void A_granted_override_can_be_revoked_before_it_expires()
    {
        var request = Granted();
        request.Revoke(Now.AddMinutes(5));

        Assert.Equal(OverrideState.Revoked, request.State);
        Assert.False(request.IsInForceAt(Now.AddMinutes(6)));
    }

    [Fact]
    public void An_override_survives_a_round_trip_through_storage()
    {
        var request = Granted(breakGlass: true);

        var restored = FreezeOverride.Rehydrate(
            request.ChangeReference, request.IncidentReference, request.Justification,
            request.RequestedBy, request.RequestedAtUtc, request.GrantDuration, request.IsBreakGlass,
            request.State, request.GrantedAtUtc, request.ExpiresAtUtc, request.UsedAtUtc,
            request.RetrospectiveDueAtUtc, request.RetrospectiveCompletedAtUtc,
            request.RetrospectiveNotes, request.Approvals);

        Assert.Equal(request.State, restored.State);
        Assert.Equal(request.ExpiresAtUtc, restored.ExpiresAtUtc);
        Assert.Equal(request.RetrospectiveDueAtUtc, restored.RetrospectiveDueAtUtc);
        Assert.Equal(request.Approvals.Count, restored.Approvals.Count);
        Assert.True(restored.IsInForceAt(Now));
    }

    private static FreezeOverride Granted(bool breakGlass = false)
    {
        var request = Request(breakGlass);

        if (breakGlass)
        {
            request.RecordApproval(By(ApprovalRole.HeadOfIt), Trackside, Now);
            return request;
        }

        request.RecordApproval(By(ApprovalRole.TracksideItLead), Trackside, Now);
        request.RecordApproval(By(ApprovalRole.HeadOfIt), Trackside, Now);
        request.RecordApproval(By(ApprovalRole.RaceEngineeringNominee), Trackside, Now);
        return request;
    }
}
