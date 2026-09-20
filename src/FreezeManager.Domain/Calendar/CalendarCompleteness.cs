namespace FreezeManager.Domain.Calendar;

/// <summary>
/// Checks a stored calendar for holes.
/// </summary>
/// <remarks>
/// A gap in the calendar is a silent gap in the freeze: a round nobody loaded is a weekend the
/// engine will happily let changes through. Decision 3 makes missing <i>data within</i> an event
/// fail safe by lengthening the freeze; this is the equivalent for an event that is missing
/// altogether, which no fallback inside the engine can cover. It has to be surfaced instead.
/// </remarks>
public static class CalendarCompleteness
{
    public static CalendarCompletenessReport Inspect(RaceCalendar calendar, int? expectedRounds = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        var active = calendar.ActiveEvents.OrderBy(e => e.Round).ToArray();

        // "Missing" means absent from the calendar, not merely inactive. A cancelled round is a
        // deliberate exclusion that someone recorded; a round nobody ever loaded is the hole.
        var rounds = calendar.Events.Select(e => e.Round).ToHashSet();
        var highest = rounds.Count == 0 ? 0 : rounds.Max();
        var target = Math.Max(highest, expectedRounds ?? 0);

        var missing = Enumerable.Range(1, Math.Max(target, 0))
            .Where(r => !rounds.Contains(r))
            .ToArray();

        var incomplete = new List<CalendarGap>();

        foreach (var raceEvent in active)
        {
            if (raceEvent.Sessions.Count == 0)
            {
                incomplete.Add(new CalendarGap(raceEvent.Round, raceEvent.Circuit, "No sessions at all."));
                continue;
            }

            if (raceEvent.RaceSession is null)
            {
                incomplete.Add(new CalendarGap(
                    raceEvent.Round, raceEvent.Circuit,
                    "No race session, so any policy anchored on the race cannot resolve."));
            }

            if (raceEvent.ParcFermeWindows.Count == 0)
            {
                incomplete.Add(new CalendarGap(
                    raceEvent.Round, raceEvent.Circuit,
                    "No parc ferme windows; the trackside freeze falls back to the last session end."));
            }

            if (string.Equals(raceEvent.LocalTimeZoneId, RaceEvent.UnresolvedTimeZoneId, StringComparison.OrdinalIgnoreCase))
            {
                incomplete.Add(new CalendarGap(
                    raceEvent.Round, raceEvent.Circuit,
                    $"Time zone unresolved, showing as {RaceEvent.UnresolvedTimeZoneId}. Freeze windows are "
                    + "unaffected because they are instants, but local times will display wrongly."));
            }
        }

        return new CalendarCompletenessReport(calendar.Season, rounds.Count, highest, missing, incomplete);
    }
}

/// <summary>One problem with one round.</summary>
public sealed record CalendarGap(int Round, string Circuit, string Problem);

public sealed class CalendarCompletenessReport
{
    public CalendarCompletenessReport(
        int season,
        int roundsPresent,
        int highestRound,
        IReadOnlyList<int> missingRounds,
        IReadOnlyList<CalendarGap> incompleteRounds)
    {
        Season = season;
        RoundsPresent = roundsPresent;
        HighestRound = highestRound;
        MissingRounds = missingRounds;
        IncompleteRounds = incompleteRounds;
    }

    public int Season { get; }

    public int RoundsPresent { get; }

    public int HighestRound { get; }

    /// <summary>
    /// Rounds absent from the stored calendar. Each one is a race weekend during which the engine
    /// currently believes nothing is frozen.
    /// </summary>
    public IReadOnlyList<int> MissingRounds { get; }

    /// <summary>Rounds that loaded but are missing something.</summary>
    public IReadOnlyList<CalendarGap> IncompleteRounds { get; }

    public bool IsComplete => MissingRounds.Count == 0 && IncompleteRounds.Count == 0;

    /// <summary>True when a whole weekend is unprotected. Worse than a round merely missing detail.</summary>
    public bool HasUnprotectedWeekends => MissingRounds.Count > 0;

    public override string ToString()
    {
        if (IsComplete)
        {
            return $"{Season}: {RoundsPresent} rounds, complete.";
        }

        var parts = new List<string>();

        if (MissingRounds.Count > 0)
        {
            parts.Add($"missing rounds {string.Join(", ", MissingRounds)}");
        }

        if (IncompleteRounds.Count > 0)
        {
            parts.Add($"{IncompleteRounds.Count} incomplete round(s)");
        }

        return $"{Season}: {RoundsPresent} rounds, {string.Join("; ", parts)}.";
    }
}
