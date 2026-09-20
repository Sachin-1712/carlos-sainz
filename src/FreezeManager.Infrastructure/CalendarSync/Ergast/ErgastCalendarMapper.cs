using System.Globalization;
using FreezeManager.Domain.Calendar;
using FreezeManager.Infrastructure.Persistence;

namespace FreezeManager.Infrastructure.CalendarSync.Ergast;

/// <summary>Turns an Ergast-shaped response into calendar records. Pure; testable without HTTP.</summary>
public static class ErgastCalendarMapper
{
    public static CalendarFetchResult Map(
        int season,
        ErgastResponse response,
        IngestionDefaults defaults,
        DateTimeOffset fetchedAtUtc,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(defaults);

        var races = response.MRData?.RaceTable?.Races ?? new List<ErgastRace>();
        var warnings = new List<string>();
        var unmappedCircuitIds = new List<string>();
        var events = new List<RaceEventRecord>();

        foreach (var race in races)
        {
            if (!int.TryParse(race.Round, NumberStyles.Integer, CultureInfo.InvariantCulture, out var round) || round < 1)
            {
                warnings.Add($"Skipped a race with an unreadable round number '{race.Round}'.");
                continue;
            }

            var sessions = new List<SessionRecord>();
            var sprintQualifying = race.SprintQualifying ?? race.SprintShootout;

            AddSession(sessions, SessionType.Practice1, race.FirstPractice, defaults, round, warnings);
            AddSession(sessions, SessionType.Practice2, race.SecondPractice, defaults, round, warnings);
            AddSession(sessions, SessionType.Practice3, race.ThirdPractice, defaults, round, warnings);
            AddSession(sessions, SessionType.SprintQualifying, sprintQualifying, defaults, round, warnings);
            AddSession(sessions, SessionType.Sprint, race.Sprint, defaults, round, warnings);
            AddSession(sessions, SessionType.Qualifying, race.Qualifying, defaults, round, warnings);
            AddSession(sessions, SessionType.Race, race.Date, race.Time, defaults, round, warnings);

            if (sessions.Count == 0)
            {
                warnings.Add($"Round {round}: no session has a published time yet; skipped.");
                continue;
            }

            var circuitId = race.Circuit?.CircuitId;

            if (!CircuitTimeZones.TryResolve(circuitId, out var timeZoneId))
            {
                unmappedCircuitIds.Add(circuitId ?? "(none published)");
                warnings.Add($"Round {round}: no time zone mapping for circuit '{circuitId ?? "?"}'; using {timeZoneId}.");
            }

            var isSprint = race.Sprint is not null || sprintQualifying is not null;

            events.Add(new RaceEventRecord
            {
                Season = season,
                Round = round,
                OfficialName = race.RaceName ?? $"Round {round}",
                Circuit = race.Circuit?.CircuitName ?? race.Circuit?.Location?.Locality ?? "Unknown circuit",
                Country = race.Circuit?.Location?.Country ?? "Unknown",
                LocalTimeZoneId = timeZoneId,
                Format = isSprint ? EventFormat.Sprint : EventFormat.Conventional,
                Status = EventStatus.Scheduled,
                Source = CalendarSource.LiveUpstream,
                IsVerified = true,
                UpstreamCircuitId = circuitId,
                SyncedAtUtc = fetchedAtUtc.UtcDateTime,
                Sessions = sessions,
                ParcFermeWindows = ParcFermeDerivation.Derive(sessions, defaults)
            });
        }

        return new CalendarFetchResult(providerName, CalendarSource.LiveUpstream, events, warnings, unmappedCircuitIds);
    }

    private static void AddSession(
        List<SessionRecord> sessions,
        SessionType type,
        ErgastSessionTime? sessionTime,
        IngestionDefaults defaults,
        int round,
        List<string> warnings)
    {
        if (sessionTime is null)
        {
            return;
        }

        AddSession(sessions, type, sessionTime.Date, sessionTime.Time, defaults, round, warnings);
    }

    private static void AddSession(
        List<SessionRecord> sessions,
        SessionType type,
        string? date,
        string? time,
        IngestionDefaults defaults,
        int round,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(time))
        {
            warnings.Add($"Round {round}: {type} has a date but no published time; omitted.");
            return;
        }

        if (!DateTimeOffset.TryParseExact(
                $"{date}T{time}",
                "yyyy-MM-dd'T'HH:mm:ssK",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var start))
        {
            warnings.Add($"Round {round}: could not read {type} time '{date} {time}'; omitted.");
            return;
        }

        sessions.Add(new SessionRecord
        {
            Type = type,
            ScheduledStartUtc = start.UtcDateTime,
            ScheduledEndUtc = (start + defaults.DurationFor(type)).UtcDateTime
        });
    }
}
