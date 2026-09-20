using FreezeManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreezeManager.Infrastructure.CalendarSync;

/// <summary>
/// Pulls a season from a provider and reconciles it into the store.
/// </summary>
/// <remarks>
/// Reconciliation rules, in order of who wins: a person's pinned event is never touched; a
/// person's recorded actual times are never touched; a person's manually entered parc ferme
/// windows are kept; everything else is refreshed from the provider.
/// </remarks>
public sealed class CalendarSyncService
{
    private readonly FreezeDbContext _db;
    private readonly IRaceCalendarProvider _provider;
    private readonly TimeProvider _time;
    private readonly ILogger<CalendarSyncService> _logger;

    public CalendarSyncService(
        FreezeDbContext db,
        IRaceCalendarProvider provider,
        TimeProvider? time = null,
        ILogger<CalendarSyncService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(provider);

        _db = db;
        _provider = provider;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CalendarSyncService>.Instance;
    }

    public async Task<CalendarSyncResult> SyncSeasonAsync(int season, CancellationToken cancellationToken = default)
    {
        var run = new CalendarSyncRunRecord
        {
            Season = season,
            Provider = _provider.Name,
            StartedAtUtc = _time.GetUtcNow().UtcDateTime
        };

        _db.CalendarSyncRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);

        CalendarFetchResult fetched;

        try
        {
            fetched = await _provider.FetchSeasonAsync(season, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Calendar sync for season {Season} failed at fetch.", season);

            run.CompletedAtUtc = _time.GetUtcNow().UtcDateTime;
            run.Succeeded = false;
            run.Message = $"{exception.GetType().Name}: {exception.Message}";
            await _db.SaveChangesAsync(cancellationToken);

            return CalendarSyncResult.Failed(season, _provider.Name, run.Message);
        }

        var existing = await _db.RaceEvents
            .Include(e => e.Sessions)
            .Include(e => e.ParcFermeWindows)
            .Where(e => e.Season == season)
            .ToDictionaryAsync(e => e.Round, cancellationToken);

        var added = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var incoming in fetched.Events)
        {
            if (existing.TryGetValue(incoming.Round, out var current))
            {
                if (current.PinnedByAdmin)
                {
                    skipped++;
                    continue;
                }

                Apply(current, incoming);
                updated++;
            }
            else
            {
                _db.RaceEvents.Add(incoming);
                added++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        run.CompletedAtUtc = _time.GetUtcNow().UtcDateTime;
        run.Succeeded = true;
        run.Source = fetched.Source;
        run.EventsAdded = added;
        run.EventsUpdated = updated;
        run.EventsSkipped = skipped;
        run.Message = fetched.Warnings.Count == 0 ? null : string.Join(" | ", fetched.Warnings);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Calendar sync for season {Season} from {Provider}: {Added} added, {Updated} updated, {Skipped} pinned, {Warnings} warnings, unmapped circuits: {Unmapped}.",
            season, fetched.ProviderName, added, updated, skipped, fetched.Warnings.Count,
            fetched.UnmappedCircuitIds.Count == 0 ? "none" : string.Join(", ", fetched.UnmappedCircuitIds));

        return new CalendarSyncResult(
            season, fetched.ProviderName, fetched.Source, added, updated, skipped,
            fetched.Warnings, fetched.UnmappedCircuitIds);
    }

    private static void Apply(RaceEventRecord current, RaceEventRecord incoming)
    {
        current.OfficialName = incoming.OfficialName;
        current.Circuit = incoming.Circuit;
        current.Country = incoming.Country;
        current.LocalTimeZoneId = incoming.LocalTimeZoneId;
        current.Format = incoming.Format;
        current.Status = incoming.Status;
        current.Source = incoming.Source;
        current.IsVerified = incoming.IsVerified;
        current.UpstreamCircuitId = incoming.UpstreamCircuitId;
        current.SyncedAtUtc = incoming.SyncedAtUtc;

        // Sessions: refresh scheduled times, keep actuals, drop anything upstream no longer lists.
        var incomingByType = incoming.Sessions.ToDictionary(s => s.Type);

        foreach (var session in current.Sessions.ToList())
        {
            if (incomingByType.TryGetValue(session.Type, out var replacement))
            {
                session.ScheduledStartUtc = replacement.ScheduledStartUtc;
                session.ScheduledEndUtc = replacement.ScheduledEndUtc;
                incomingByType.Remove(session.Type);
            }
            else
            {
                current.Sessions.Remove(session);
            }
        }

        foreach (var fresh in incomingByType.Values)
        {
            current.Sessions.Add(new SessionRecord
            {
                Type = fresh.Type,
                ScheduledStartUtc = fresh.ScheduledStartUtc,
                ScheduledEndUtc = fresh.ScheduledEndUtc
            });
        }

        // Parc ferme: derived windows are the rule's and get replaced; manual ones are a person's and stay.
        foreach (var derived in current.ParcFermeWindows.Where(w => w.IsDerived).ToList())
        {
            current.ParcFermeWindows.Remove(derived);
        }

        foreach (var window in incoming.ParcFermeWindows)
        {
            current.ParcFermeWindows.Add(new ParcFermeWindowRecord
            {
                StartUtc = window.StartUtc,
                EndUtc = window.EndUtc,
                Label = window.Label,
                IsDerived = window.IsDerived
            });
        }
    }
}

public sealed class CalendarSyncResult
{
    public CalendarSyncResult(
        int season,
        string providerName,
        CalendarSource? source,
        int added,
        int updated,
        int skippedPinned,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> unmappedCircuitIds,
        string? error = null)
    {
        Season = season;
        ProviderName = providerName;
        Source = source;
        Added = added;
        Updated = updated;
        SkippedPinned = skippedPinned;
        Warnings = warnings;
        UnmappedCircuitIds = unmappedCircuitIds;
        Error = error;
    }

    public static CalendarSyncResult Failed(int season, string providerName, string error) =>
        new(season, providerName, null, 0, 0, 0, Array.Empty<string>(), Array.Empty<string>(), error);

    public int Season { get; }

    public string ProviderName { get; }

    public CalendarSource? Source { get; }

    public int Added { get; }

    public int Updated { get; }

    public int SkippedPinned { get; }

    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Circuits with no time zone mapping. Each one is a row to add to the lookup table.</summary>
    public IReadOnlyList<string> UnmappedCircuitIds { get; }

    public string? Error { get; }

    public bool Succeeded => Error is null;
}
