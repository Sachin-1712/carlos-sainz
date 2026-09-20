using FreezeManager.Domain.Changes;
using Xunit;

namespace FreezeManager.Domain.Tests.Changes;

public class ChangeStateMachineTests
{
    [Theory]
    [InlineData(ChangeState.Draft, ChangeState.Submitted)]
    [InlineData(ChangeState.Draft, ChangeState.Cancelled)]
    [InlineData(ChangeState.Submitted, ChangeState.Approved)]
    [InlineData(ChangeState.Submitted, ChangeState.Rejected)]
    [InlineData(ChangeState.Approved, ChangeState.Scheduled)]
    [InlineData(ChangeState.Rejected, ChangeState.Draft)]
    [InlineData(ChangeState.Submitted, ChangeState.Draft)]
    [InlineData(ChangeState.Scheduled, ChangeState.Implementing)]
    [InlineData(ChangeState.Implementing, ChangeState.Implemented)]
    [InlineData(ChangeState.Implementing, ChangeState.Failed)]
    [InlineData(ChangeState.Implemented, ChangeState.Closed)]
    [InlineData(ChangeState.Failed, ChangeState.RolledBack)]
    [InlineData(ChangeState.RolledBack, ChangeState.Closed)]
    public void Legal_moves_are_allowed(ChangeState from, ChangeState to)
    {
        Assert.True(ChangeStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ChangeState.Submitted, ChangeState.Scheduled)]    // cannot skip the approval chain
    [InlineData(ChangeState.Rejected, ChangeState.Scheduled)]     // a rejection never proceeds
    [InlineData(ChangeState.Rejected, ChangeState.Approved)]      // nor is it approved after the fact
    [InlineData(ChangeState.Draft, ChangeState.Approved)]         // approval requires a submission
    [InlineData(ChangeState.Draft, ChangeState.Scheduled)]        // cannot skip submission
    [InlineData(ChangeState.Draft, ChangeState.Implementing)]     // cannot skip the gate entirely
    [InlineData(ChangeState.Submitted, ChangeState.Implementing)] // cannot implement an unscheduled change
    [InlineData(ChangeState.Scheduled, ChangeState.Implemented)]  // cannot finish what was never started
    [InlineData(ChangeState.Implementing, ChangeState.Cancelled)] // cannot cancel mid-implementation
    [InlineData(ChangeState.Failed, ChangeState.Closed)]          // a failure is backed out or reworked, never just closed
    [InlineData(ChangeState.Implemented, ChangeState.Draft)]
    public void Illegal_moves_are_rejected(ChangeState from, ChangeState to)
    {
        Assert.False(ChangeStateMachine.CanTransition(from, to));
        Assert.Throws<InvalidChangeTransitionException>(() => ChangeStateMachine.EnsureCanTransition(from, to));
    }

    [Theory]
    [InlineData(ChangeState.Closed)]
    [InlineData(ChangeState.Cancelled)]
    public void Terminal_states_go_nowhere(ChangeState state)
    {
        Assert.True(ChangeStateMachine.IsTerminal(state));
        Assert.Empty(ChangeStateMachine.NextStatesFrom(state));
    }

    [Fact]
    public void A_change_can_never_move_to_itself()
    {
        foreach (var state in Enum.GetValues<ChangeState>())
        {
            Assert.False(ChangeStateMachine.CanTransition(state, state), $"{state} -> {state}");
        }
    }

    [Fact]
    public void Every_state_is_reachable_from_draft()
    {
        // Guards against adding a state to the enum and forgetting to wire it into the table.
        var reachable = new HashSet<ChangeState> { ChangeState.Draft };
        var queue = new Queue<ChangeState>([ChangeState.Draft]);

        while (queue.Count > 0)
        {
            foreach (var next in ChangeStateMachine.NextStatesFrom(queue.Dequeue()))
            {
                if (reachable.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        Assert.Equal(Enum.GetValues<ChangeState>().OrderBy(s => s), reachable.OrderBy(s => s));
    }

    [Fact]
    public void Only_submission_and_scheduling_require_the_freeze_gate()
    {
        Assert.True(ChangeStateMachine.RequiresFreezeCheck(ChangeState.Draft, ChangeState.Submitted));
        Assert.True(ChangeStateMachine.RequiresFreezeCheck(ChangeState.Approved, ChangeState.Scheduled));

        // Approving a change agrees to the work, not to the slot. The slot is re-checked when it
        // is confirmed, so approval itself does not re-run the gate.
        Assert.False(ChangeStateMachine.RequiresFreezeCheck(ChangeState.Submitted, ChangeState.Approved));

        Assert.False(ChangeStateMachine.RequiresFreezeCheck(ChangeState.Scheduled, ChangeState.Implementing));
        Assert.False(ChangeStateMachine.RequiresFreezeCheck(ChangeState.Draft, ChangeState.Cancelled));
        Assert.False(ChangeStateMachine.RequiresFreezeCheck(ChangeState.Submitted, ChangeState.Draft));
    }

    [Fact]
    public void Every_gated_transition_is_also_a_legal_one()
    {
        foreach (var from in Enum.GetValues<ChangeState>())
        {
            foreach (var to in Enum.GetValues<ChangeState>())
            {
                if (ChangeStateMachine.RequiresFreezeCheck(from, to))
                {
                    Assert.True(ChangeStateMachine.CanTransition(from, to), $"gated but illegal: {from} -> {to}");
                }
            }
        }
    }
}
