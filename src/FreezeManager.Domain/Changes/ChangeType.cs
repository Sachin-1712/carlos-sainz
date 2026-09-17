namespace FreezeManager.Domain.Changes;

/// <summary>How a change is routed. Standard ITSM classification.</summary>
public enum ChangeType
{
    /// <summary>Pre-approved, low risk, repeatable. Still subject to the freeze.</summary>
    Standard = 0,

    /// <summary>Assessed and scheduled. The ordinary path.</summary>
    Normal = 1,

    /// <summary>
    /// Raised against a live incident. In this phase it is gated exactly like any other change:
    /// the override that lets an emergency through a freeze arrives with the approval chain.
    /// </summary>
    Emergency = 2
}
