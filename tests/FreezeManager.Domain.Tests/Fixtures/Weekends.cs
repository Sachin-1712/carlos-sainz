using FreezeManager.Domain.Calendar;

namespace FreezeManager.Domain.Tests.Fixtures;

/// <summary>
/// Race weekends built from a single anchor instant, so every expectation in a test can be written
/// as an offset from FP1 rather than as a calendar date nobody can check by eye.
/// </summary>
/// <remarks>
/// Layout of a conventional weekend, relative to the FP1 start (T):
/// <code>
///   FP1          T      .. T+1h
///   FP2          T+4h   .. T+5h
///   FP3          T+22h  .. T+23h
///   Qualifying   T+25h  .. T+26h
///   Race         T+48h  .. T+50h
///   Parc ferme   T+25h  .. T+53h
/// </code>
/// </remarks>
internal static class Weekends
{
    public const string DefaultTimeZoneId = "Europe/London";

    /// <summary>FP1 for the reference weekend: Friday 8 May 2026, 12:00 UTC.</summary>
    public static readonly DateTimeOffset ReferenceFp1 = new(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);

    public static RaceEvent Conventional(
        int round,
        DateTimeOffset fp1Start,
        string circuit = "Testville",
        string country = "Testland",
        string timeZoneId = DefaultTimeZoneId,
        EventStatus status = EventStatus.Scheduled,
        bool withParcFerme = true)
    {
        var sessions = new[]
        {
            new Session(SessionType.Practice1, fp1Start, fp1Start.AddHours(1)),
            new Session(SessionType.Practice2, fp1Start.AddHours(4), fp1Start.AddHours(5)),
            new Session(SessionType.Practice3, fp1Start.AddHours(22), fp1Start.AddHours(23)),
            new Session(SessionType.Qualifying, fp1Start.AddHours(25), fp1Start.AddHours(26)),
            new Session(SessionType.Race, fp1Start.AddHours(48), fp1Start.AddHours(50))
        };

        var parcFerme = withParcFerme
            ? new[] { new ParcFermeWindow(fp1Start.AddHours(25), fp1Start.AddHours(53), "race parc ferme") }
            : Array.Empty<ParcFermeWindow>();

        return new RaceEvent(
            round,
            $"Round {round} Grand Prix",
            circuit,
            country,
            timeZoneId,
            EventFormat.Conventional,
            sessions,
            parcFerme,
            status);
    }

    /// <summary>
    /// A sprint weekend with two separate parc ferme windows -- the shape that exists because the
    /// regulations were revised to let teams change setup between the sprint and qualifying.
    /// <code>
    ///   FP1               T      .. T+1h
    ///   Sprint qualifying T+4h   .. T+5h
    ///   Sprint            T+22h  .. T+23h
    ///   Qualifying        T+26h  .. T+27h
    ///   Race              T+48h  .. T+50h
    ///   Parc ferme        T+4h   .. T+23h   (sprint)
    ///   Parc ferme        T+26h  .. T+53h   (race)
    /// </code>
    /// </summary>
    public static RaceEvent Sprint(
        int round,
        DateTimeOffset fp1Start,
        string circuit = "Sprintville",
        string country = "Testland",
        string timeZoneId = DefaultTimeZoneId)
    {
        var sessions = new[]
        {
            new Session(SessionType.Practice1, fp1Start, fp1Start.AddHours(1)),
            new Session(SessionType.SprintQualifying, fp1Start.AddHours(4), fp1Start.AddHours(5)),
            new Session(SessionType.Sprint, fp1Start.AddHours(22), fp1Start.AddHours(23)),
            new Session(SessionType.Qualifying, fp1Start.AddHours(26), fp1Start.AddHours(27)),
            new Session(SessionType.Race, fp1Start.AddHours(48), fp1Start.AddHours(50))
        };

        var parcFerme = new[]
        {
            new ParcFermeWindow(fp1Start.AddHours(4), fp1Start.AddHours(23), "sprint parc ferme"),
            new ParcFermeWindow(fp1Start.AddHours(26), fp1Start.AddHours(53), "race parc ferme")
        };

        return new RaceEvent(
            round,
            $"Round {round} Grand Prix",
            circuit,
            country,
            timeZoneId,
            EventFormat.Sprint,
            sessions,
            parcFerme);
    }

    public static RaceCalendar Calendar(params RaceEvent[] events) => new(2026, events);

    /// <summary>A single reference weekend, rounds numbered from 1.</summary>
    public static RaceCalendar SingleConventionalWeekend() =>
        Calendar(Conventional(1, ReferenceFp1));

    /// <summary>Rounds one week apart, which is what a back-to-back actually looks like.</summary>
    public static RaceCalendar ConsecutiveWeekends(int count)
    {
        var events = new RaceEvent[count];

        for (var i = 0; i < count; i++)
        {
            events[i] = Conventional(i + 1, ReferenceFp1.AddDays(7 * i), $"Circuit {i + 1}");
        }

        return Calendar(events);
    }
}
