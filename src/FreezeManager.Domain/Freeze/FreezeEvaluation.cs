using FreezeManager.Domain.Services;

namespace FreezeManager.Domain.Freeze;

/// <summary>The freeze position of a service tier at a single instant.</summary>
public sealed class FreezeEvaluation
{
    public FreezeEvaluation(
        DateTimeOffset evaluatedAtUtc,
        ServiceTier tier,
        string policyName,
        FreezeWindow? activeFreezeWindow,
        FreezeWindow? nextFreezeWindow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);

        EvaluatedAtUtc = evaluatedAtUtc.ToUniversalTime();
        Tier = tier;
        PolicyName = policyName;
        ActiveFreezeWindow = activeFreezeWindow;
        NextFreezeWindow = nextFreezeWindow;
    }

    public DateTimeOffset EvaluatedAtUtc { get; }

    public ServiceTier Tier { get; }

    public string PolicyName { get; }

    /// <summary>The freeze window in force right now, if any.</summary>
    public FreezeWindow? ActiveFreezeWindow { get; }

    /// <summary>
    /// The next FREEZE window to begin after <see cref="EvaluatedAtUtc"/>, if any. Not the next open
    /// window -- that is <see cref="FreezeCalculator.NextOpenWindow(Services.ServiceTier, DateTimeOffset, TimeSpan, TimeSpan?)"/>.
    /// </summary>
    public FreezeWindow? NextFreezeWindow { get; }

    /// <summary>Changes are blocked.</summary>
    public bool IsFrozen => ActiveFreezeWindow is not null && !ActiveFreezeWindow.IsAdvisory;

    /// <summary>A window is in force but only warns.</summary>
    public bool IsAdvisory => ActiveFreezeWindow is not null && ActiveFreezeWindow.IsAdvisory;

    /// <summary>No window in force at all.</summary>
    public bool IsOpen => ActiveFreezeWindow is null;

    public string? Reason => ActiveFreezeWindow?.Reason;

    /// <summary>How long until the current window lifts. Null when nothing is in force.</summary>
    public TimeSpan? TimeUntilThaw
    {
        get
        {
            var active = ActiveFreezeWindow;
            return active is null ? null : active.EndUtc - EvaluatedAtUtc;
        }
    }

    /// <summary>How long until the next window starts. Null while one is already in force.</summary>
    public TimeSpan? TimeUntilNextFreeze
    {
        get
        {
            var next = NextFreezeWindow;

            if (ActiveFreezeWindow is not null || next is null)
            {
                return null;
            }

            return next.StartUtc - EvaluatedAtUtc;
        }
    }

    public override string ToString()
    {
        if (IsFrozen)
        {
            return $"{Tier}: FROZEN until {ActiveFreezeWindow!.EndUtc:yyyy-MM-dd HH:mm}Z -- {ActiveFreezeWindow.Reason}";
        }

        return IsAdvisory
            ? $"{Tier}: advisory -- {ActiveFreezeWindow!.Reason}"
            : $"{Tier}: open";
    }
}
