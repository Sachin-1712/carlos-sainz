using FreezeManager.Domain.Services;

namespace FreezeManager.Domain.Freeze;

public enum ChangeWindowVerdict
{
    Allowed = 0,

    /// <summary>Clashes only with advisory windows: proceed, but the requester is told why not to.</summary>
    AllowedWithWarning = 1,

    /// <summary>Clashes with a blocking window. Needs rescheduling, or an emergency override.</summary>
    BlockedByFreeze = 2
}

/// <summary>The verdict on a proposed change window. This is what the change request gate calls.</summary>
public sealed class ChangeWindowAssessment
{
    public ChangeWindowAssessment(
        ChangeWindowVerdict verdict,
        IEnumerable<ServiceTier> tiers,
        DateTimeOffset requestedStartUtc,
        DateTimeOffset requestedEndUtc,
        IEnumerable<FreezeWindow> conflicts,
        OpenWindow? suggestedAlternative)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        ArgumentNullException.ThrowIfNull(conflicts);

        Verdict = verdict;
        Tiers = tiers.Distinct().OrderBy(t => t).ToArray();
        RequestedStartUtc = requestedStartUtc.ToUniversalTime();
        RequestedEndUtc = requestedEndUtc.ToUniversalTime();
        Conflicts = conflicts.ToArray();
        SuggestedAlternative = suggestedAlternative;
    }

    public ChangeWindowVerdict Verdict { get; }

    /// <summary>Every tier the change touches, strictest first.</summary>
    public IReadOnlyList<ServiceTier> Tiers { get; }

    public DateTimeOffset RequestedStartUtc { get; }

    public DateTimeOffset RequestedEndUtc { get; }

    /// <summary>The windows the request collided with, in start order.</summary>
    public IReadOnlyList<FreezeWindow> Conflicts { get; }

    /// <summary>
    /// The next window of at least the requested duration. Offering this alongside the refusal is
    /// what turns the tool from a blocker into a planning aid -- and is the difference between a
    /// process people use and a process people learn to route around.
    /// </summary>
    public OpenWindow? SuggestedAlternative { get; }

    public bool IsBlocked => Verdict == ChangeWindowVerdict.BlockedByFreeze;

    public TimeSpan RequestedDuration => RequestedEndUtc - RequestedStartUtc;

    public override string ToString() => Verdict switch
    {
        ChangeWindowVerdict.Allowed => "Allowed",
        ChangeWindowVerdict.AllowedWithWarning => $"Allowed with warning: {Conflicts.Count} advisory window(s)",
        _ => $"Blocked by {Conflicts.Count} freeze window(s)"
    };
}
