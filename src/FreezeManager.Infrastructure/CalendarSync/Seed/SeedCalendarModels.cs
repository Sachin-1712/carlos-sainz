using FreezeManager.Domain.Calendar;

namespace FreezeManager.Infrastructure.CalendarSync.Seed;

// The bundled seed file's own schema. Enums are written as names, times as ISO-8601 UTC.

public sealed class SeedCalendarDocument
{
    public int Season { get; set; }

    public string? Disclaimer { get; set; }

    public List<SeedEvent>? Events { get; set; }
}

public sealed class SeedEvent
{
    public int Round { get; set; }

    public string? OfficialName { get; set; }

    public string? Circuit { get; set; }

    public string? Country { get; set; }

    public string? LocalTimeZoneId { get; set; }

    public EventFormat Format { get; set; }

    public EventStatus Status { get; set; }

    /// <summary>False unless a person has checked this row against the official calendar.</summary>
    public bool Verified { get; set; }

    public List<SeedSession>? Sessions { get; set; }

    /// <summary>Optional. When absent, parc ferme is derived from the sessions and marked as such.</summary>
    public List<SeedParcFerme>? ParcFerme { get; set; }
}

public sealed class SeedSession
{
    public SessionType Type { get; set; }

    public DateTimeOffset Start { get; set; }

    public DateTimeOffset End { get; set; }

    public DateTimeOffset? ActualStart { get; set; }

    public DateTimeOffset? ActualEnd { get; set; }
}

public sealed class SeedParcFerme
{
    public DateTimeOffset Start { get; set; }

    public DateTimeOffset End { get; set; }

    public string? Label { get; set; }

    public bool Derived { get; set; }
}
