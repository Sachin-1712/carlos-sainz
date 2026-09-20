namespace FreezeManager.Domain.Changes;

/// <summary>
/// Where a change is in its lifecycle.
/// </summary>
/// <remarks>
/// <see cref="Approved"/> and <see cref="Rejected"/> were inserted between <see cref="Submitted"/>
/// and <see cref="Scheduled"/> when the approval chain landed. Because the transitions are a table
/// rather than a set of conditionals, that was an edit to the table.
/// </remarks>
public enum ChangeState
{
    /// <summary>Being written. The only state in which a change can be edited or deleted.</summary>
    Draft = 0,

    /// <summary>Handed in and past the freeze gate. Awaiting its approval chain.</summary>
    Submitted = 1,

    /// <summary>The approval chain is satisfied. Awaiting a confirmed window.</summary>
    Approved = 9,

    /// <summary>An approver said no. Reworked as a draft, or cancelled.</summary>
    Rejected = 10,

    /// <summary>Has a confirmed window that cleared the freeze gate.</summary>
    Scheduled = 2,

    /// <summary>Being carried out.</summary>
    Implementing = 3,

    /// <summary>Carried out, awaiting closure.</summary>
    Implemented = 4,

    /// <summary>Implementation failed. Either backed out or reworked.</summary>
    Failed = 5,

    /// <summary>Backed out using the backout plan.</summary>
    RolledBack = 6,

    /// <summary>Terminal. Complete.</summary>
    Closed = 7,

    /// <summary>Terminal. Withdrawn before implementation.</summary>
    Cancelled = 8
}
