namespace FreezeManager.Domain.Freeze;

/// <summary>
/// A period during which changes to a service are frozen.
/// </summary>
/// <remarks>
/// Windows are <b>half-open</b> intervals: <c>[StartUtc, EndUtc)</c>. The instant a window ends is
/// the first instant that is no longer frozen, which means two back-to-back windows never both
/// claim the boundary instant and "frozen until 14:00" reads the way an engineer expects it to.
/// </remarks>
public sealed class FreezeWindow
{
    public FreezeWindow(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string reason,
        bool isAdvisory = false,
        IEnumerable<int>? rounds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (endUtc <= startUtc)
        {
            throw new ArgumentException(
                $"A freeze window must end after it starts (start: {startUtc:O}, end: {endUtc:O}).",
                nameof(endUtc));
        }

        StartUtc = startUtc.ToUniversalTime();
        EndUtc = endUtc.ToUniversalTime();
        Reason = reason;
        IsAdvisory = isAdvisory;
        Rounds = (rounds ?? Array.Empty<int>()).Distinct().Order().ToArray();
    }

    /// <summary>First frozen instant, in UTC.</summary>
    public DateTimeOffset StartUtc { get; }

    /// <summary>First instant that is no longer frozen, in UTC. Exclusive.</summary>
    public DateTimeOffset EndUtc { get; }

    /// <summary>Human-readable explanation, surfaced directly to the engineer who was blocked.</summary>
    public string Reason { get; }

    /// <summary>
    /// When true this window warns but does not block. Tier 3 (corporate) services use advisory
    /// windows: marking every service business-critical is how a freeze process loses credibility.
    /// </summary>
    public bool IsAdvisory { get; }

    /// <summary>Championship rounds that contributed to this window. Multiple after a merge.</summary>
    public IReadOnlyList<int> Rounds { get; }

    public TimeSpan Duration => EndUtc - StartUtc;

    public bool Contains(DateTimeOffset instant) => instant >= StartUtc && instant < EndUtc;

    /// <summary>
    /// True when a proposed change window <c>[startUtc, endUtc)</c> intersects this freeze window at
    /// all. A change that merely <i>spans</i> a freeze is still blocked -- it is not truncated.
    /// </summary>
    public bool Overlaps(DateTimeOffset startUtc, DateTimeOffset endUtc) =>
        startUtc < EndUtc && endUtc > StartUtc;

    public bool Overlaps(FreezeWindow other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Overlaps(other.StartUtc, other.EndUtc);
    }

    public override string ToString() =>
        $"[{StartUtc:yyyy-MM-dd HH:mm}Z .. {EndUtc:yyyy-MM-dd HH:mm}Z) {Reason}"
        + (IsAdvisory ? " (advisory)" : string.Empty);
}
