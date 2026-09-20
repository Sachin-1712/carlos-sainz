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
        IEnumerable<string>? warnings = null,
        IEnumerable<string>? unmappedCircuitIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(events);

        ProviderName = providerName;
        Source = source;
        Events = events.OrderBy(e => e.Round).ToArray();
        Warnings = (warnings ?? Array.Empty<string>()).ToArray();
        UnmappedCircuitIds = (unmappedCircuitIds ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string ProviderName { get; }

    public CalendarSource Source { get; }

    public IReadOnlyList<RaceEventRecord> Events { get; }

    /// <summary>Things that were skipped, defaulted or guessed. Surfaced in the sync history.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Circuit identifiers with no time zone mapping, which fell back to UTC.
    /// </summary>
    /// <remarks>
    /// A field of its own rather than only a line in <see cref="Warnings"/>: the fix is to add a row
    /// to the lookup table, so the thing to add should be readable without parsing prose.
    /// </remarks>
    public IReadOnlyList<string> UnmappedCircuitIds { get; }
}
