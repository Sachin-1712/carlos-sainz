using FreezeManager.Domain.Approvals;
using FreezeManager.Domain.Changes;
using FreezeManager.Domain.Services;
using Xunit;

namespace FreezeManager.Domain.Tests.Changes;

public class ChangeApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Window = new(2026, 5, 1, 20, 0, 0, TimeSpan.Zero);

    private static readonly ApprovalRequirement Trackside =
        ApprovalRequirement.For(new[] { ServiceTier.Trackside }, ChangeType.Normal);

    private static ChangeRequest Submitted()
    {
        var change = new ChangeRequest(
            ChangeReference.Create(2026, 1), "Patch", "s.sindhe", ChangeType.Normal,
            ChangeImpact.Medium, ChangeLikelihood.Low, new[] { "telemetry-ingest" },
            Window, Window.AddHours(4), Now, "d", "i", "b");

        change.TransitionTo(ChangeState.Submitted, Now);
        return change;
    }

    private static ChangeApproval By(ApprovalRole role, ApprovalDecision decision = ApprovalDecision.Approved) =>
        new(role, role.ToString(), decision, Now);

    [Fact]
    public void An_approval_can_only_be_recorded_while_the_change_is_awaiting_one()
    {
        var change = Submitted();
        change.TransitionTo(ChangeState.Draft, Now);

        Assert.Throws<ChangeValidationException>(() =>
            change.RecordApproval(By(ApprovalRole.HeadOfIt), Trackside, Now));
    }

    [Fact]
    public void A_role_not_on_the_chain_cannot_approve()
    {
        var change = Submitted();

        var exception = Assert.Throws<ChangeValidationException>(() =>
            change.RecordApproval(By(ApprovalRole.ServiceOwner), Trackside, Now));

        Assert.Contains("not on the approval chain", exception.Message);
    }

    [Fact]
    public void A_role_cannot_approve_twice()
    {
        var change = Submitted();
        change.RecordApproval(By(ApprovalRole.HeadOfIt), Trackside, Now);

        Assert.Throws<ChangeValidationException>(() =>
            change.RecordApproval(By(ApprovalRole.HeadOfIt), Trackside, Now));
    }

    [Fact]
    public void The_chain_completes_only_when_all_three_trackside_roles_have_approved()
    {
        var change = Submitted();

        change.RecordApproval(By(ApprovalRole.TracksideItLead), Trackside, Now);
        Assert.False(Trackside.IsSatisfiedBy(change.Approvals));

        change.RecordApproval(By(ApprovalRole.HeadOfIt), Trackside, Now);
        Assert.False(Trackside.IsSatisfiedBy(change.Approvals));

        change.RecordApproval(By(ApprovalRole.RaceEngineeringNominee), Trackside, Now);
        Assert.True(Trackside.IsSatisfiedBy(change.Approvals));
    }

    [Fact]
    public void Withdrawing_to_draft_discards_approvals_so_nobody_approves_one_thing_and_gets_another()
    {
        var change = Submitted();
        change.RecordApproval(By(ApprovalRole.TracksideItLead), Trackside, Now);
        change.RecordApproval(By(ApprovalRole.HeadOfIt), Trackside, Now);

        change.TransitionTo(ChangeState.Draft, Now);

        Assert.Empty(change.Approvals);

        // And the chain starts again from nothing on the next submission.
        change.TransitionTo(ChangeState.Submitted, Now);
        Assert.Equal(3, Trackside.OutstandingRoles(change.Approvals).Count);
    }

    [Fact]
    public void A_rejection_is_recorded_alongside_the_approvals_that_preceded_it()
    {
        var change = Submitted();
        change.RecordApproval(By(ApprovalRole.TracksideItLead), Trackside, Now);
        change.RecordApproval(By(ApprovalRole.HeadOfIt, ApprovalDecision.Rejected), Trackside, Now);

        Assert.Equal(2, change.Approvals.Count);
        Assert.True(ApprovalRequirement.IsRejected(change.Approvals));
    }

    [Fact]
    public void Approvals_survive_a_round_trip_through_storage()
    {
        var change = Submitted();
        var stored = new[] { By(ApprovalRole.TracksideItLead), By(ApprovalRole.HeadOfIt) };

        change.RehydrateApprovals(stored);

        Assert.Equal(2, change.Approvals.Count);
        Assert.Equal(new[] { ApprovalRole.RaceEngineeringNominee }, Trackside.OutstandingRoles(change.Approvals));
    }
}
