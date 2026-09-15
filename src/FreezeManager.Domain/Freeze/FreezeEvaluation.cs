using FreezeManager.Domain.Services;

namespace FreezeManager.Domain.Freeze;

/// <summary>The freeze position of a service tier at a single instant.</summary>
public sealed class FreezeEvaluation
{
    public FreezeEvaluation(
        DateTimeOffset evaluatedAtUtc,
        ServiceTier tier,
        string policyName,
        FreezeWindow? activeWindow,
        FreezeWindow? nextWindow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);

        EvaluatedAtUtc = evaluatedAtUtc.ToUniversalTime();
        Tier = tier;
        PolicyName = policyName;
        ActiveWindow = activeWindow;
        NextWindow = nextWindow;
    }

    public DateTimeOffset EvaluatedAtUtc { get; }

    public ServiceTier Tier { get; }

    public string PolicyName { get; }

    /// <summary>The window in force right now, if any.</summary>
    public FreezeWindow? ActiveWindow { get; }

    /// <summary>The next window to start after <see cref="EvaluatedAtUtc"/>, if any.</summary>
    public FreezeWindow? NextWindow { get; }

    /// <summary>Changes are blocked.</summary>
    public bool IsFrozen => ActiveWindow is not null && !ActiveWindow.IsAdvisory;

    /// <summary>A window is in force but only warns.</summary>
    public bool IsAdvisory => ActiveWindow is not null && ActiveWindow.IsAdvisory;

    /// <summary>No window in force at all.</summary>
    public bool IsOpen => ActiveWindow is null;

    public string? Reason => ActiveWindow?.Reason;

    /// <summary>How long until the current window lifts. Null when nothing is in force.</summary>
    public TimeSpan? TimeUntilThaw
    {
        get
        {
            var active = ActiveWindow;
            return active is null ? null : active.EndUtc - EvaluatedAtUtc;
        }
    }

    /// <summary>How long until the next window starts. Null while one is already in force.</summary>
    public TimeSpan? TimeUntilNextFreeze
    {
        get
        {
            var next = NextWindow;

            if (ActiveWindow is not null || next is null)
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
            return $"{Tier}: FROZEN until {ActiveWindow!.EndUtc:yyyy-MM-dd HH:mm}Z -- {ActiveWindow.Reason}";
        }

        return IsAdvisory
            ? $"{Tier}: advisory -- {ActiveWindow!.Reason}"
            : $"{Tier}: open";
    }
}
