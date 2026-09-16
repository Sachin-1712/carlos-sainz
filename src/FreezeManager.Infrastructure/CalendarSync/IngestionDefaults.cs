using FreezeManager.Domain.Calendar;

namespace FreezeManager.Infrastructure.CalendarSync;

/// <summary>
/// What ingestion fills in when the source does not publish it. The calendar API gives session
/// start times only: no durations, no parc ferme. These are conservative -- a session slot
/// slightly longer than the running is a freeze that ends slightly late, which fails safe.
/// </summary>
public sealed record IngestionDefaults
{
    public static IngestionDefaults Standard { get; } = new();

    public TimeSpan PracticeDuration { get; init; } = TimeSpan.FromMinutes(60);

    public TimeSpan SprintQualifyingDuration { get; init; } = TimeSpan.FromMinutes(60);

    public TimeSpan SprintDuration { get; init; } = TimeSpan.FromMinutes(60);

    public TimeSpan QualifyingDuration { get; init; } = TimeSpan.FromMinutes(60);

    public TimeSpan RaceDuration { get; init; } = TimeSpan.FromHours(2);

    /// <summary>How long after the scheduled race end the cars are assumed to be released.</summary>
    public TimeSpan ParcFermeReleaseAfterRace { get; init; } = TimeSpan.FromHours(3);

    public TimeSpan DurationFor(SessionType type) => type switch
    {
        SessionType.Practice1 or SessionType.Practice2 or SessionType.Practice3 => PracticeDuration,
        SessionType.SprintQualifying => SprintQualifyingDuration,
        SessionType.Sprint => SprintDuration,
        SessionType.Qualifying => QualifyingDuration,
        SessionType.Race => RaceDuration,
        _ => PracticeDuration
    };
}
