using FreezeManager.Domain.Freeze;
using FreezeManager.Domain.Services;

namespace FreezeManager.Domain.Changes;

public enum FreezeGateOutcome
{
    /// <summary>Nothing in the way.</summary>
    Allowed = 0,

    /// <summary>Only advisory windows clash. It may proceed, and the requester is told why not to.</summary>
    AllowedWithWarning = 1,

    /// <summary>A blocking freeze window clashes.</summary>
    Blocked = 2,

    /// <summary>A named service is not in the catalogue, so no tier could be resolved.</summary>
    UnknownService = 3
}

/// <summary>The gate's answer, including the way forward when the answer is no.</summary>
public sealed class FreezeGateDecision
{
    private FreezeGateDecision(
        FreezeGateOutcome outcome,
        IReadOnlyList<ServiceTier> tiers,
        IReadOnlyList<FreezeWindow> conflicts,
        OpenWindow? suggestedWindow,
        IReadOnlyList<string> unknownServiceKeys)
    {
        Outcome = outcome;
        Tiers = tiers;
        Conflicts = conflicts;
        SuggestedWindow = suggestedWindow;
        UnknownServiceKeys = unknownServiceKeys;
    }

    public FreezeGateOutcome Outcome { get; }

    /// <summary>Tiers the change touches, strictest first.</summary>
    public IReadOnlyList<ServiceTier> Tiers { get; }

    /// <summary>The windows the request collided with, in start order.</summary>
    public IReadOnlyList<FreezeWindow> Conflicts { get; }

    /// <summary>
    /// The next window long enough for the change. Present whenever the gate blocks and such a
    /// window exists inside the horizon; null when none does, which is itself worth saying.
    /// </summary>
    public OpenWindow? SuggestedWindow { get; }

    public IReadOnlyList<string> UnknownServiceKeys { get; }

    public bool IsAllowed => Outcome is FreezeGateOutcome.Allowed or FreezeGateOutcome.AllowedWithWarning;

    public static FreezeGateDecision From(ChangeWindowAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        var outcome = assessment.Verdict switch
        {
            ChangeWindowVerdict.Allowed => FreezeGateOutcome.Allowed,
            ChangeWindowVerdict.AllowedWithWarning => FreezeGateOutcome.AllowedWithWarning,
            _ => FreezeGateOutcome.Blocked
        };

        return new FreezeGateDecision(
            outcome,
            assessment.Tiers,
            assessment.Conflicts,
            assessment.SuggestedAlternative,
            Array.Empty<string>());
    }

    public static FreezeGateDecision UnknownServices(IEnumerable<string> keys) =>
        new(
            FreezeGateOutcome.UnknownService,
            Array.Empty<ServiceTier>(),
            Array.Empty<FreezeWindow>(),
            null,
            keys.ToArray());

    public override string ToString() => Outcome switch
    {
        FreezeGateOutcome.Allowed => "Allowed",
        FreezeGateOutcome.AllowedWithWarning => $"Allowed with {Conflicts.Count} advisory window(s)",
        FreezeGateOutcome.UnknownService => $"Unknown service(s): {string.Join(", ", UnknownServiceKeys)}",
        _ => SuggestedWindow is null
            ? $"Blocked by {Conflicts.Count} freeze window(s); no window found inside the horizon"
            : $"Blocked by {Conflicts.Count} freeze window(s); next window opens {SuggestedWindow.StartUtc:yyyy-MM-dd HH:mm}Z"
    };
}
