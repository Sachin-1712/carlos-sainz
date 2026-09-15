namespace FreezeManager.Domain.Freeze;

/// <summary>
/// Collapses overlapping and touching freeze windows into a canonical, non-overlapping set.
/// </summary>
/// <remarks>
/// This is the piece that makes "when can I actually deploy?" answerable. A Tier 1 policy with a
/// 48-hour lead-in overlaps the previous event's tail during a back-to-back, and a triple-header
/// produces three windows with no usable gap between them. Merging turns that into one honest
/// answer instead of three windows an engineer has to reconcile by hand.
/// </remarks>
public static class FreezeWindowMerger
{
    /// <summary>
    /// Merges the supplied windows. Windows that merely touch (<c>next.Start == current.End</c>) are
    /// merged too: a zero-length gap is not a deployment opportunity.
    /// </summary>
    /// <remarks>
    /// Blocking dominates advisory. A merged window is advisory only when every window that went
    /// into it was advisory, so merging can never silently downgrade a hard freeze. Callers that
    /// need the two kinds kept apart should partition before merging.
    /// </remarks>
    public static IReadOnlyList<FreezeWindow> Merge(IEnumerable<FreezeWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var ordered = windows
            .OrderBy(w => w.StartUtc)
            .ThenBy(w => w.EndUtc)
            .ToList();

        if (ordered.Count == 0)
        {
            return Array.Empty<FreezeWindow>();
        }

        var merged = new List<FreezeWindow>();

        var start = ordered[0].StartUtc;
        var end = ordered[0].EndUtc;
        var advisory = ordered[0].IsAdvisory;
        var rounds = new List<int>(ordered[0].Rounds);
        var reasons = new List<string> { ordered[0].Reason };

        for (var i = 1; i < ordered.Count; i++)
        {
            var next = ordered[i];

            if (next.StartUtc <= end)
            {
                if (next.EndUtc > end)
                {
                    end = next.EndUtc;
                }

                advisory = advisory && next.IsAdvisory;
                rounds.AddRange(next.Rounds);
                reasons.Add(next.Reason);
            }
            else
            {
                merged.Add(Build(start, end, reasons, advisory, rounds));

                start = next.StartUtc;
                end = next.EndUtc;
                advisory = next.IsAdvisory;
                rounds = new List<int>(next.Rounds);
                reasons = new List<string> { next.Reason };
            }
        }

        merged.Add(Build(start, end, reasons, advisory, rounds));
        return merged;
    }

    private static FreezeWindow Build(
        DateTimeOffset start,
        DateTimeOffset end,
        List<string> reasons,
        bool isAdvisory,
        List<int> rounds)
    {
        var distinct = reasons.Distinct().ToArray();
        var reason = distinct.Length == 1 ? distinct[0] : string.Join(" + ", distinct);

        return new FreezeWindow(start, end, reason, isAdvisory, rounds);
    }
}
