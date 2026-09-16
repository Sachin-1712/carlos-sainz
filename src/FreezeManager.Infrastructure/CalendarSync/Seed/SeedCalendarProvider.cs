using System.Text.Json;
using System.Text.Json.Serialization;
using FreezeManager.Infrastructure.Persistence;

namespace FreezeManager.Infrastructure.CalendarSync.Seed;

/// <summary>
/// The bundled calendar. Cold-start fallback for when the live provider is unavailable and nothing
/// has been synced yet. Rows carry whatever verification flag the file says, which for the shipped
/// file is false throughout.
/// </summary>
public sealed class SeedCalendarProvider : IRaceCalendarProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly IngestionDefaults _defaults;
    private readonly TimeProvider _time;

    public SeedCalendarProvider(string path, IngestionDefaults? defaults = null, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _defaults = defaults ?? IngestionDefaults.Standard;
        _time = time ?? TimeProvider.System;
    }

    public string Name => $"Seed file ({Path.GetFileName(_path)})";

    public async Task<CalendarFetchResult> FetchSeasonAsync(int season, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(_path);

        var document = await JsonSerializer.DeserializeAsync<SeedCalendarDocument>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Seed calendar at '{_path}' is empty.");

        if (document.Season != season)
        {
            return new CalendarFetchResult(
                Name,
                CalendarSource.Seed,
                Array.Empty<RaceEventRecord>(),
                new[] { $"Seed file covers season {document.Season}, not {season}; nothing loaded." });
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var warnings = new List<string>();
        var events = new List<RaceEventRecord>();

        foreach (var seed in document.Events ?? new List<SeedEvent>())
        {
            var sessions = (seed.Sessions ?? new List<SeedSession>())
                .Select(s => new SessionRecord
                {
                    Type = s.Type,
                    ScheduledStartUtc = UtcTime.FromOffset(s.Start),
                    ScheduledEndUtc = UtcTime.FromOffset(s.End),
                    ActualStartUtc = UtcTime.FromOffset(s.ActualStart),
                    ActualEndUtc = UtcTime.FromOffset(s.ActualEnd)
                })
                .ToList();

            var parcFerme = seed.ParcFerme is { Count: > 0 }
                ? seed.ParcFerme.Select(w => new ParcFermeWindowRecord
                {
                    StartUtc = UtcTime.FromOffset(w.Start),
                    EndUtc = UtcTime.FromOffset(w.End),
                    Label = w.Label,
                    IsDerived = w.Derived
                }).ToList()
                : ParcFermeDerivation.Derive(sessions, _defaults);

            if (!seed.Verified)
            {
                warnings.Add($"Round {seed.Round}: seeded dates are unverified.");
            }

            events.Add(new RaceEventRecord
            {
                Season = season,
                Round = seed.Round,
                OfficialName = seed.OfficialName ?? $"Round {seed.Round}",
                Circuit = seed.Circuit ?? "Unknown circuit",
                Country = seed.Country ?? "Unknown",
                LocalTimeZoneId = seed.LocalTimeZoneId ?? CircuitTimeZones.Unknown,
                Format = seed.Format,
                Status = seed.Status,
                Source = CalendarSource.Seed,
                IsVerified = seed.Verified,
                SyncedAtUtc = now,
                Sessions = sessions,
                ParcFermeWindows = parcFerme
            });
        }

        return new CalendarFetchResult(Name, CalendarSource.Seed, events, warnings);
    }
}
