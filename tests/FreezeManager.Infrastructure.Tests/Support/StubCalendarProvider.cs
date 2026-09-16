using FreezeManager.Domain.Calendar;
using FreezeManager.Infrastructure.CalendarSync;
using FreezeManager.Infrastructure.Persistence;

namespace FreezeManager.Infrastructure.Tests.Support;

/// <summary>Returns whatever it was given, or throws whatever it was told to.</summary>
internal sealed class StubCalendarProvider : IRaceCalendarProvider
{
    private readonly Func<int, CalendarFetchResult> _fetch;

    public StubCalendarProvider(string name, Func<int, CalendarFetchResult> fetch)
    {
        Name = name;
        _fetch = fetch;
    }

    public string Name { get; }

    public int Calls { get; private set; }

    public Task<CalendarFetchResult> FetchSeasonAsync(int season, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(_fetch(season));
    }

    public static StubCalendarProvider Returning(string name, CalendarSource source, params RaceEventRecord[] events) =>
        new(name, _ => new CalendarFetchResult(name, source, events));

    public static StubCalendarProvider Throwing(string name, Exception exception) =>
        new(name, _ => throw exception);

    /// <summary>A three-session weekend record with derived parc ferme, FP1 at the given instant.</summary>
    public static RaceEventRecord Weekend(int round, DateTime fp1Utc, CalendarSource source = CalendarSource.LiveUpstream)
    {
        var sessions = new List<SessionRecord>
        {
            new() { Type = SessionType.Practice1, ScheduledStartUtc = fp1Utc, ScheduledEndUtc = fp1Utc.AddHours(1) },
            new() { Type = SessionType.Qualifying, ScheduledStartUtc = fp1Utc.AddHours(25), ScheduledEndUtc = fp1Utc.AddHours(26) },
            new() { Type = SessionType.Race, ScheduledStartUtc = fp1Utc.AddHours(48), ScheduledEndUtc = fp1Utc.AddHours(50) }
        };

        return new RaceEventRecord
        {
            Season = 2026,
            Round = round,
            OfficialName = $"Round {round} Grand Prix",
            Circuit = $"Circuit {round}",
            Country = "Testland",
            LocalTimeZoneId = "Europe/London",
            Format = EventFormat.Conventional,
            Status = EventStatus.Scheduled,
            Source = source,
            IsVerified = source == CalendarSource.LiveUpstream,
            SyncedAtUtc = fp1Utc.AddDays(-30),
            Sessions = sessions,
            ParcFermeWindows = ParcFermeDerivation.Derive(sessions, IngestionDefaults.Standard)
        };
    }
}
