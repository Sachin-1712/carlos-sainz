namespace FreezeManager.Domain.Approvals;

/// <summary>
/// Who has to say yes. Roles, not people: a chain written in names stops working the day someone
/// leaves.
/// </summary>
public enum ApprovalRole
{
    /// <summary>Accountable for the affected service.</summary>
    ServiceOwner = 0,

    HeadOfIt = 1,

    /// <summary>Accountable for the kit at the circuit.</summary>
    TracksideItLead = 2,

    /// <summary>
    /// Speaks for the race engineering side. On the chain for trackside changes because touching a
    /// trackside system during a live session has sporting consequences, not only IT ones.
    /// </summary>
    RaceEngineeringNominee = 3
}

public enum ApprovalDecision
{
    Approved = 0,
    Rejected = 1
}
