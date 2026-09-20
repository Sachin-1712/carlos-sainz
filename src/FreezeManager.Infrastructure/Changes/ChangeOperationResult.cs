using FreezeManager.Domain.Changes;
using FreezeManager.Domain.Overrides;

namespace FreezeManager.Infrastructure.Changes;

public enum ChangeOperationStatus
{
    Succeeded = 0,

    /// <summary>No change with that reference.</summary>
    NotFound = 1,

    /// <summary>The move is not legal from the change's current state.</summary>
    IllegalTransition = 2,

    /// <summary>The change is not complete enough, or the request was malformed.</summary>
    Invalid = 3,

    /// <summary>The freeze gate said no. The decision carries the next open window.</summary>
    BlockedByFreeze = 4
}

/// <summary>
/// The outcome of an operation on a change, with enough detail for the caller to explain it.
/// </summary>
public sealed class ChangeOperationResult
{
    private ChangeOperationResult(
        ChangeOperationStatus status,
        ChangeRequest? change,
        FreezeGateDecision? gateDecision,
        IReadOnlyList<string> problems,
        FreezeOverride? usedOverride = null)
    {
        Status = status;
        Change = change;
        GateDecision = gateDecision;
        Problems = problems;
        UsedOverride = usedOverride;
    }

    public ChangeOperationStatus Status { get; }

    public ChangeRequest? Change { get; }

    /// <summary>Present whenever the gate ran, whether it allowed or blocked.</summary>
    public FreezeGateDecision? GateDecision { get; }

    public IReadOnlyList<string> Problems { get; }

    /// <summary>
    /// Set when the freeze gate refused and a granted override carried the change through anyway.
    /// Its presence is the difference between "the gate allowed this" and "the gate refused and
    /// someone accountable overruled it", which a caller must not have to infer.
    /// </summary>
    public FreezeOverride? UsedOverride { get; }

    public bool Succeeded => Status == ChangeOperationStatus.Succeeded;

    public static ChangeOperationResult Ok(
        ChangeRequest change,
        FreezeGateDecision? decision = null,
        FreezeOverride? usedOverride = null) =>
        new(ChangeOperationStatus.Succeeded, change, decision, Array.Empty<string>(), usedOverride);

    public static ChangeOperationResult NotFound(string reference) =>
        new(ChangeOperationStatus.NotFound, null, null, new[] { $"No change with reference '{reference}'." });

    public static ChangeOperationResult IllegalTransition(ChangeRequest change, ChangeState target) =>
        new(
            ChangeOperationStatus.IllegalTransition,
            change,
            null,
            new[] { $"A change cannot move from {change.State} to {target}." });

    public static ChangeOperationResult Invalid(ChangeRequest? change, IEnumerable<string> problems) =>
        new(ChangeOperationStatus.Invalid, change, null, problems.ToArray());

    public static ChangeOperationResult Blocked(ChangeRequest change, FreezeGateDecision decision) =>
        new(ChangeOperationStatus.BlockedByFreeze, change, decision, new[] { decision.ToString() });
}
