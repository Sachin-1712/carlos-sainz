using FreezeManager.Domain.Changes;
using FreezeManager.Domain.Services;

namespace FreezeManager.Domain.Approvals;

/// <summary>
/// Who has to approve a change, derived from the tiers it touches and how it was raised.
/// </summary>
/// <remarks>
/// The chain scales with exposure: a corporate change needs its service owner, a race support
/// change adds the head of IT, and a trackside change adds race engineering, because the
/// consequences of getting it wrong stop being only an IT problem. The strictest tier a change
/// touches sets the chain, for the same reason the strictest tier sets the freeze (decision 22).
/// </remarks>
public sealed class ApprovalRequirement
{
    private static readonly ApprovalRole[] None = Array.Empty<ApprovalRole>();

    private ApprovalRequirement(IReadOnlyList<ApprovalRole> roles, string rationale)
    {
        Roles = roles;
        Rationale = rationale;
    }

    /// <summary>Roles that must all record an approval. Empty for a pre-approved standard change.</summary>
    public IReadOnlyList<ApprovalRole> Roles { get; }

    /// <summary>Why this chain, in words the requester can read.</summary>
    public string Rationale { get; }

    public bool IsPreApproved => Roles.Count == 0;

    /// <summary>
    /// Roles that can carry a break-glass grant alone. Deliberately narrow: the point of
    /// break-glass is that one accountable person can be reached at 02:00, not that anyone can.
    /// </summary>
    public static IReadOnlyList<ApprovalRole> BreakGlassRoles { get; } =
        new[] { ApprovalRole.HeadOfIt, ApprovalRole.TracksideItLead };

    public static ApprovalRequirement For(IEnumerable<ServiceTier> tiers, ChangeType changeType)
    {
        ArgumentNullException.ThrowIfNull(tiers);

        var tierList = tiers.Distinct().ToArray();

        if (tierList.Length == 0)
        {
            return new ApprovalRequirement(
                new[] { ApprovalRole.HeadOfIt },
                "No service tier could be resolved, so the change is escalated rather than waved through.");
        }

        // A standard change is pre-approved by definition. It is still subject to the freeze, and
        // it is still audited.
        if (changeType == ChangeType.Standard)
        {
            return new ApprovalRequirement(
                None,
                "Standard change: pre-approved. It remains subject to the freeze and is audited.");
        }

        var strictest = tierList.Min();

        return strictest switch
        {
            ServiceTier.Trackside => new ApprovalRequirement(
                new[] { ApprovalRole.TracksideItLead, ApprovalRole.HeadOfIt, ApprovalRole.RaceEngineeringNominee },
                "Touches a trackside service: trackside IT lead, head of IT and a race engineering nominee."),

            ServiceTier.RaceSupport => new ApprovalRequirement(
                new[] { ApprovalRole.ServiceOwner, ApprovalRole.HeadOfIt },
                "Touches a race support service: service owner and head of IT."),

            _ => new ApprovalRequirement(
                new[] { ApprovalRole.ServiceOwner },
                "Corporate services only: the service owner.")
        };
    }

    /// <summary>Roles that have not yet recorded an approval.</summary>
    public IReadOnlyList<ApprovalRole> OutstandingRoles(IEnumerable<ChangeApproval> approvals)
    {
        ArgumentNullException.ThrowIfNull(approvals);

        var approved = approvals.Where(a => a.IsApproval).Select(a => a.Role).ToHashSet();
        return Roles.Where(r => !approved.Contains(r)).ToArray();
    }

    public bool IsSatisfiedBy(IEnumerable<ChangeApproval> approvals) => OutstandingRoles(approvals).Count == 0;

    public static bool IsRejected(IEnumerable<ChangeApproval> approvals)
    {
        ArgumentNullException.ThrowIfNull(approvals);
        return approvals.Any(a => a.Decision == ApprovalDecision.Rejected);
    }
}
