using FreezeManager.Domain.Approvals;
using FreezeManager.Domain.Changes;
using FreezeManager.Domain.Services;
using Xunit;

namespace FreezeManager.Domain.Tests.Approvals;

public class ApprovalRequirementTests
{
    private static readonly DateTimeOffset T = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    private static ChangeApproval Approval(ApprovalRole role, ApprovalDecision decision = ApprovalDecision.Approved) =>
        new(role, "a.person", decision, T);

    [Fact]
    public void A_corporate_change_needs_only_its_service_owner()
    {
        var requirement = ApprovalRequirement.For(new[] { ServiceTier.Corporate }, ChangeType.Normal);

        Assert.Equal(new[] { ApprovalRole.ServiceOwner }, requirement.Roles);
    }

    [Fact]
    public void A_race_support_change_adds_the_head_of_it()
    {
        var requirement = ApprovalRequirement.For(new[] { ServiceTier.RaceSupport }, ChangeType.Normal);

        Assert.Equal(new[] { ApprovalRole.ServiceOwner, ApprovalRole.HeadOfIt }, requirement.Roles);
    }

    [Fact]
    public void A_trackside_change_adds_race_engineering_because_the_consequences_are_sporting()
    {
        var requirement = ApprovalRequirement.For(new[] { ServiceTier.Trackside }, ChangeType.Normal);

        Assert.Equal(
            new[] { ApprovalRole.TracksideItLead, ApprovalRole.HeadOfIt, ApprovalRole.RaceEngineeringNominee },
            requirement.Roles);
    }

    [Fact]
    public void The_chain_scales_with_the_strictest_tier_a_change_touches()
    {
        // Same rule as the freeze itself: a corporate service in the list cannot soften it.
        var requirement = ApprovalRequirement.For(
            new[] { ServiceTier.Corporate, ServiceTier.Trackside }, ChangeType.Normal);

        Assert.Contains(ApprovalRole.RaceEngineeringNominee, requirement.Roles);
        Assert.Equal(3, requirement.Roles.Count);
    }

    [Fact]
    public void A_standard_change_is_pre_approved_but_still_subject_to_the_freeze()
    {
        var requirement = ApprovalRequirement.For(new[] { ServiceTier.Trackside }, ChangeType.Standard);

        Assert.True(requirement.IsPreApproved);
        Assert.Empty(requirement.Roles);
        Assert.Contains("subject to the freeze", requirement.Rationale);
    }

    [Fact]
    public void An_emergency_change_takes_the_same_chain_as_its_tier()
    {
        var emergency = ApprovalRequirement.For(new[] { ServiceTier.Trackside }, ChangeType.Emergency);
        var normal = ApprovalRequirement.For(new[] { ServiceTier.Trackside }, ChangeType.Normal);

        Assert.Equal(normal.Roles, emergency.Roles);
    }

    [Fact]
    public void A_change_whose_tier_cannot_be_resolved_is_escalated_rather_than_waved_through()
    {
        var requirement = ApprovalRequirement.For(Array.Empty<ServiceTier>(), ChangeType.Normal);

        Assert.Equal(new[] { ApprovalRole.HeadOfIt }, requirement.Roles);
        Assert.False(requirement.IsPreApproved);
    }

    [Fact]
    public void A_chain_is_satisfied_only_when_every_role_has_approved()
    {
        var requirement = ApprovalRequirement.For(new[] { ServiceTier.Trackside }, ChangeType.Normal);

        var partial = new[] { Approval(ApprovalRole.TracksideItLead), Approval(ApprovalRole.HeadOfIt) };
        Assert.False(requirement.IsSatisfiedBy(partial));
        Assert.Equal(new[] { ApprovalRole.RaceEngineeringNominee }, requirement.OutstandingRoles(partial));

        var complete = partial.Append(Approval(ApprovalRole.RaceEngineeringNominee)).ToArray();
        Assert.True(requirement.IsSatisfiedBy(complete));
        Assert.Empty(requirement.OutstandingRoles(complete));
    }

    [Fact]
    public void A_rejection_counts_even_when_the_other_roles_approved()
    {
        var requirement = ApprovalRequirement.For(new[] { ServiceTier.RaceSupport }, ChangeType.Normal);

        var approvals = new[]
        {
            Approval(ApprovalRole.ServiceOwner),
            Approval(ApprovalRole.HeadOfIt, ApprovalDecision.Rejected)
        };

        Assert.False(requirement.IsSatisfiedBy(approvals));
        Assert.True(ApprovalRequirement.IsRejected(approvals));
    }

    [Fact]
    public void A_pre_approved_change_is_satisfied_by_no_approvals_at_all()
    {
        var requirement = ApprovalRequirement.For(new[] { ServiceTier.Corporate }, ChangeType.Standard);

        Assert.True(requirement.IsSatisfiedBy(Array.Empty<ChangeApproval>()));
    }
}
