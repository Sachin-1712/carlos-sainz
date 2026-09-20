namespace FreezeManager.Domain.Changes;

/// <summary>
/// The legal moves in the change lifecycle, as a table.
/// </summary>
/// <remarks>
/// A table rather than a chain of conditionals: the legal moves are then readable in one place,
/// testable exhaustively, and extensible by editing data. Inserting the approval states between
/// <see cref="ChangeState.Submitted"/> and <see cref="ChangeState.Scheduled"/> is a change to this
/// table and nothing else.
/// </remarks>
public static class ChangeStateMachine
{
    private static readonly Dictionary<ChangeState, ChangeState[]> Allowed = new()
    {
        [ChangeState.Draft] = new[] { ChangeState.Submitted, ChangeState.Cancelled },

        // Back to draft is a withdrawal for rework, which is a normal thing to want.
        [ChangeState.Submitted] = new[]
        {
            ChangeState.Approved, ChangeState.Rejected, ChangeState.Draft, ChangeState.Cancelled
        },

        [ChangeState.Approved] = new[] { ChangeState.Scheduled, ChangeState.Draft, ChangeState.Cancelled },

        // A rejection is reworked or abandoned. It never proceeds.
        [ChangeState.Rejected] = new[] { ChangeState.Draft, ChangeState.Cancelled },

        [ChangeState.Scheduled] = new[] { ChangeState.Implementing, ChangeState.Approved, ChangeState.Cancelled },

        [ChangeState.Implementing] = new[] { ChangeState.Implemented, ChangeState.Failed },

        [ChangeState.Implemented] = new[] { ChangeState.Closed },

        // A failure is either backed out or reworked from scratch. It is never quietly closed.
        [ChangeState.Failed] = new[] { ChangeState.RolledBack, ChangeState.Draft },

        [ChangeState.RolledBack] = new[] { ChangeState.Closed },

        [ChangeState.Closed] = Array.Empty<ChangeState>(),

        [ChangeState.Cancelled] = Array.Empty<ChangeState>()
    };

    /// <summary>States a change may move to from here. Empty for terminal states.</summary>
    public static IReadOnlyList<ChangeState> NextStatesFrom(ChangeState state) =>
        Allowed.TryGetValue(state, out var next) ? next : Array.Empty<ChangeState>();

    public static bool CanTransition(ChangeState from, ChangeState to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);

    public static bool IsTerminal(ChangeState state) => NextStatesFrom(state).Count == 0;

    public static void EnsureCanTransition(ChangeState from, ChangeState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidChangeTransitionException(from, to);
        }
    }

    /// <summary>
    /// Transitions that must clear the freeze gate before they are allowed.
    /// </summary>
    /// <remarks>
    /// Submission and scheduling: the two points at which a window is being claimed. Approval sits
    /// between them and does not re-run the gate, because approving a change is agreeing to the
    /// work, not to the slot -- the slot is re-checked when it is confirmed.
    /// </remarks>
    public static bool RequiresFreezeCheck(ChangeState from, ChangeState to) =>
        (from, to) switch
        {
            (ChangeState.Draft, ChangeState.Submitted) => true,
            (ChangeState.Approved, ChangeState.Scheduled) => true,
            _ => false
        };
}
