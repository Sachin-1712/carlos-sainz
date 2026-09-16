using FreezeManager.Domain.Calendar;
using FreezeManager.Domain.Services;

namespace FreezeManager.Infrastructure.Persistence;

// Persistence records are deliberately separate from the domain objects. The domain constructors
// validate and expose read-only state; EF Core wants parameterless construction and setters.
// Sharing one class means one side compromises, and it is always the domain that loses. Each
// record's ToDomain() is where validation happens on the way out of the store.

public sealed class RaceEventRecord
{
    public int Id { get; set; }

    public int Season { get; set; }

    public int Round { get; set; }

    public string OfficialName { get; set; } = string.Empty;

    public string Circuit { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string LocalTimeZoneId { get; set; } = "Etc/UTC";

    public EventFormat Format { get; set; }

    public EventStatus Status { get; set; }

    public CalendarSource Source { get; set; }

    /// <summary>
    /// True when the dates came from a published upstream source or were confirmed by a person.
    /// The bundled seed is false throughout.
    /// </summary>
    public bool IsVerified { get; set; }

    /// <summary>When set, automatic syncs leave this event alone. A person's correction beats upstream.</summary>
    public bool PinnedByAdmin { get; set; }

    /// <summary>The upstream API's identifier for the circuit, when known. Used for time zone lookup.</summary>
    public string? UpstreamCircuitId { get; set; }

    public DateTime SyncedAtUtc { get; set; }

    public List<SessionRecord> Sessions { get; set; } = new();

    public List<ParcFermeWindowRecord> ParcFermeWindows { get; set; } = new();

    public RaceEvent ToDomain() => new(
        Round,
        OfficialName,
        Circuit,
        Country,
        LocalTimeZoneId,
        Format,
        Sessions.Select(s => s.ToDomain()),
        ParcFermeWindows.Select(w => w.ToDomain()),
        Status);
}

public sealed class SessionRecord
{
    public int Id { get; set; }

    public int RaceEventId { get; set; }

    public SessionType Type { get; set; }

    public DateTime ScheduledStartUtc { get; set; }

    public DateTime ScheduledEndUtc { get; set; }

    public DateTime? ActualStartUtc { get; set; }

    public DateTime? ActualEndUtc { get; set; }

    public Session ToDomain() => new(
        Type,
        UtcTime.ToOffset(ScheduledStartUtc),
        UtcTime.ToOffset(ScheduledEndUtc),
        UtcTime.ToOffset(ActualStartUtc),
        UtcTime.ToOffset(ActualEndUtc));
}

public sealed class ParcFermeWindowRecord
{
    public int Id { get; set; }

    public int RaceEventId { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public string? Label { get; set; }

    /// <summary>
    /// True when ingestion produced this window from a default rule because the source did not
    /// publish one. Derived windows are replaced on the next sync; manual ones are kept.
    /// </summary>
    public bool IsDerived { get; set; }

    public ParcFermeWindow ToDomain() => new(
        UtcTime.ToOffset(StartUtc),
        UtcTime.ToOffset(EndUtc),
        Label);
}

public sealed class ServiceRecord
{
    public int Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ServiceTier Tier { get; set; }

    public string Owner { get; set; } = string.Empty;

    public ManagedService ToDomain() => new(Key, Name, Tier, Owner);
}

/// <summary>One row per sync attempt, successful or not. This is the operational history.</summary>
public sealed class CalendarSyncRunRecord
{
    public int Id { get; set; }

    public int Season { get; set; }

    public string Provider { get; set; } = string.Empty;

    public CalendarSource? Source { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public bool Succeeded { get; set; }

    public int EventsAdded { get; set; }

    public int EventsUpdated { get; set; }

    public int EventsSkipped { get; set; }

    public string? Message { get; set; }
}
