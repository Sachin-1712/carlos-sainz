using FreezeManager.Infrastructure.Persistence;

namespace FreezeManager.Infrastructure.CalendarSync;

/// <summary>A source of race calendar data for one season.</summary>
public interface IRaceCalendarProvider
{
    string Name { get; }

    Task<CalendarFetchResult> FetchSeasonAsync(int season, CancellationToken cancellationToken = default);
}

/// <summary>What a provider produced: unattached records plus anything it wants a human to know.</summary>
public sealed class CalendarFetchResult
{
    public CalendarFetchResult(
        string providerName,
        CalendarSource source,
        IEnumerable<RaceEventRecord> events,
        IEnumerable<string>? warnings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(events);

        ProviderName = providerName;
        Source = source;
        Events = events.OrderBy(e => e.Round).ToArray();
        Warnings = (warnings ?? Array.Empty<string>()).ToArray();
    }

    public string ProviderName { get; }

    public CalendarSource Source { get; }

    public IReadOnlyList<RaceEventRecord> Events { get; }

    /// <summary>Things that were skipped, defaulted or guessed. Surfaced in the sync history.</summary>
    public IReadOnlyList<string> Warnings { get; }
}
