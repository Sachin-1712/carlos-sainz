namespace FreezeManager.Domain.Calendar;

/// <summary>A season's race events, ordered by round.</summary>
public sealed class RaceCalendar
{
    public RaceCalendar(int season, IEnumerable<RaceEvent> events)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(season, 1950);
        ArgumentNullException.ThrowIfNull(events);

        var ordered = events.OrderBy(e => e.Round).ToArray();

        var duplicateRounds = ordered
            .GroupBy(e => e.Round)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.ToString())
            .ToArray();

        if (duplicateRounds.Length > 0)
        {
            throw new ArgumentException(
                $"Season {season} lists the same round more than once: {string.Join(", ", duplicateRounds)}.",
                nameof(events));
        }

        Season = season;
        Events = ordered;
    }

    public int Season { get; }

    /// <summary>Every event, cancelled ones included, in round order.</summary>
    public IReadOnlyList<RaceEvent> Events { get; }

    /// <summary>Events that will actually take place. This is what the freeze engine reads.</summary>
    public IEnumerable<RaceEvent> ActiveEvents => Events.Where(e => !e.IsCancelled);

    public RaceEvent? EventByRound(int round) => Events.FirstOrDefault(e => e.Round == round);

    public RaceEvent? NextEventAfter(DateTimeOffset instantUtc)
    {
        var instant = instantUtc.ToUniversalTime();

        return ActiveEvents
            .Where(e => e.FirstSessionStartUtc.HasValue && e.FirstSessionStartUtc.Value > instant)
            .OrderBy(e => e.FirstSessionStartUtc!.Value)
            .FirstOrDefault();
    }

    public override string ToString() => $"{Season} season, {Events.Count} rounds";
}
