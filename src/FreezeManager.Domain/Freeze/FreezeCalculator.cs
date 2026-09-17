using FreezeManager.Domain.Calendar;
using FreezeManager.Domain.Services;

namespace FreezeManager.Domain.Freeze;

/// <summary>
/// Answers the three questions the rest of the system asks: is this service frozen right now, may
/// this change run in this window, and if not, when can it?
/// </summary>
/// <remarks>
/// Deliberately a pure function of (calendar, tier, instant). No clock, no database, no HTTP. The
/// caller supplies the instant, which is what makes every scenario below reproducible in a test
/// rather than dependent on when the suite happens to run.
/// </remarks>
public sealed class FreezeCalculator
{
    /// <summary>How far ahead <see cref="NextOpenWindow"/> looks before giving up.</summary>
    public static readonly TimeSpan DefaultHorizon = TimeSpan.FromDays(180);

    private readonly RaceCalendar _calendar;
    private readonly FreezePolicySet _policies;

    public FreezeCalculator(RaceCalendar calendar, FreezePolicySet? policies = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        _calendar = calendar;
        _policies = policies ?? FreezePolicySet.Default;
    }

    /// <summary>
    /// Every freeze window for a tier across the whole calendar, merged into a canonical set.
    /// </summary>
    /// <remarks>
    /// Blocking and advisory windows are merged separately, so an advisory window overlapping a
    /// blocking one cannot widen the blocking one, and a blocking window cannot be softened.
    /// </remarks>
    public IReadOnlyList<FreezeWindow> WindowsFor(ServiceTier tier) => WindowsFor(new[] { tier });

    /// <summary>
    /// Every freeze window across a set of tiers, merged into one canonical set.
    /// </summary>
    /// <remarks>
    /// A change usually touches more than one service, and those services need not share a tier.
    /// Merging across all of their tiers gives the constraint the change actually faces, rather
    /// than the constraint of whichever service happened to be listed first.
    /// </remarks>
    public IReadOnlyList<FreezeWindow> WindowsFor(IEnumerable<ServiceTier> tiers)
    {
        ArgumentNullException.ThrowIfNull(tiers);

        var distinct = tiers.Distinct().ToArray();

        if (distinct.Length == 0)
        {
            return Array.Empty<FreezeWindow>();
        }

        var raw = new List<FreezeWindow>();

        foreach (var tier in distinct)
        {
            var policy = _policies.For(tier);

            if (policy is null)
            {
                continue;
            }

            foreach (var raceEvent in _calendar.ActiveEvents)
            {
                raw.AddRange(policy.WindowsFor(raceEvent));
            }
        }

        var blocking = FreezeWindowMerger.Merge(raw.Where(w => !w.IsAdvisory));
        var advisory = FreezeWindowMerger.Merge(raw.Where(w => w.IsAdvisory));

        return blocking
            .Concat(advisory)
            .OrderBy(w => w.StartUtc)
            .ThenBy(w => w.EndUtc)
            .ToArray();
    }

    /// <summary>Freeze windows for a tier that intersect the given range.</summary>
    public IReadOnlyList<FreezeWindow> WindowsFor(ServiceTier tier, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (toUtc <= fromUtc)
        {
            throw new ArgumentException("Range must end after it starts.", nameof(toUtc));
        }

        return WindowsFor(tier).Where(w => w.Overlaps(fromUtc, toUtc)).ToArray();
    }

    /// <summary>Where a tier stands at a single instant.</summary>
    public FreezeEvaluation Evaluate(ServiceTier tier, DateTimeOffset atUtc)
    {
        var at = atUtc.ToUniversalTime();
        var policy = _policies.For(tier);
        var windows = WindowsFor(tier);

        // A blocking window wins over an advisory one covering the same instant: the engineer needs
        // to be told the stronger constraint, not the first one found.
        var active = windows
            .Where(w => w.Contains(at))
            .OrderBy(w => w.IsAdvisory ? 1 : 0)
            .FirstOrDefault();

        var next = windows
            .Where(w => w.StartUtc > at)
            .OrderBy(w => w.StartUtc)
            .ThenBy(w => w.IsAdvisory ? 1 : 0)
            .FirstOrDefault();

        return new FreezeEvaluation(at, tier, policy?.Name ?? "No policy configured", active, next);
    }

    /// <summary>
    /// Whether a change may run in the proposed window. A change that merely spans a freeze is
    /// blocked, never silently truncated to fit.
    /// </summary>
    public ChangeWindowAssessment AssessChangeWindow(
        ServiceTier tier,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc) => AssessChangeWindow(new[] { tier }, startUtc, endUtc);

    /// <summary>Whether a change touching several tiers may run in the proposed window.</summary>
    public ChangeWindowAssessment AssessChangeWindow(
        IEnumerable<ServiceTier> tiers,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        ArgumentNullException.ThrowIfNull(tiers);

        if (endUtc <= startUtc)
        {
            throw new ArgumentException("A change window must end after it starts.", nameof(endUtc));
        }

        var tierList = tiers.Distinct().ToArray();
        var start = startUtc.ToUniversalTime();
        var end = endUtc.ToUniversalTime();

        var conflicts = WindowsFor(tierList)
            .Where(w => w.Overlaps(start, end))
            .OrderBy(w => w.StartUtc)
            .ToArray();

        if (conflicts.Length == 0)
        {
            return new ChangeWindowAssessment(
                ChangeWindowVerdict.Allowed, tierList, start, end, conflicts, null);
        }

        var blocking = conflicts.Where(w => !w.IsAdvisory).ToArray();

        if (blocking.Length == 0)
        {
            return new ChangeWindowAssessment(
                ChangeWindowVerdict.AllowedWithWarning, tierList, start, end, conflicts, null);
        }

        var alternative = NextOpenWindow(tierList, start, end - start);

        return new ChangeWindowAssessment(
            ChangeWindowVerdict.BlockedByFreeze, tierList, start, end, blocking, alternative);
    }

    /// <summary>
    /// The next period of at least <paramref name="minimumDuration"/> in which the tier is not
    /// blocked. Advisory windows are ignored here: they warn, they do not consume the window.
    /// </summary>
    /// <returns>Null when no such period exists inside the horizon.</returns>
    public OpenWindow? NextOpenWindow(
        ServiceTier tier,
        DateTimeOffset afterUtc,
        TimeSpan minimumDuration,
        TimeSpan? horizon = null) => NextOpenWindow(new[] { tier }, afterUtc, minimumDuration, horizon);

    /// <summary>The next period long enough for a change, open across every tier it touches.</summary>
    public OpenWindow? NextOpenWindow(
        IEnumerable<ServiceTier> tiers,
        DateTimeOffset afterUtc,
        TimeSpan minimumDuration,
        TimeSpan? horizon = null)
    {
        ArgumentNullException.ThrowIfNull(tiers);

        if (minimumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumDuration), minimumDuration, "A change needs a positive duration.");
        }

        var after = afterUtc.ToUniversalTime();
        var limit = after + (horizon ?? DefaultHorizon);

        // WindowsFor has already merged these, so they neither overlap nor touch.
        var blocking = WindowsFor(tiers)
            .Where(w => !w.IsAdvisory && w.EndUtc > after && w.StartUtc < limit)
            .OrderBy(w => w.StartUtc)
            .ToArray();

        var cursor = after;

        foreach (var window in blocking)
        {
            if (window.StartUtc - cursor >= minimumDuration)
            {
                return new OpenWindow(cursor, window.StartUtc);
            }

            if (window.EndUtc > cursor)
            {
                cursor = window.EndUtc;
            }

            if (cursor >= limit)
            {
                return null;
            }
        }

        return limit - cursor >= minimumDuration ? new OpenWindow(cursor, limit) : null;
    }
}
