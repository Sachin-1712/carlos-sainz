using FreezeManager.Domain.Calendar;
using FreezeManager.Domain.Changes;
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

public sealed class ChangeRequestRecord
{
    public int Id { get; set; }

    /// <summary>Denormalised form of the reference, e.g. CHG-2026-0001. Unique, and how it is looked up.</summary>
    public string Reference { get; set; } = string.Empty;

    public int ReferenceYear { get; set; }

    public int ReferenceSequence { get; set; }

    public string Title { get; set; } = string.Empty;

    public string RequestedBy { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? ImplementationPlan { get; set; }

    public string? BackoutPlan { get; set; }

    public ChangeType Type { get; set; }

    public ChangeImpact Impact { get; set; }

    public ChangeLikelihood Likelihood { get; set; }

    public DateTime RequestedStartUtc { get; set; }

    public DateTime RequestedEndUtc { get; set; }

    public ChangeState State { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public List<ChangeAffectedServiceRecord> AffectedServices { get; set; } = new();

    public ChangeRequest ToDomain() => new(
        ChangeReference.Create(ReferenceYear, ReferenceSequence),
        Title,
        RequestedBy,
        Type,
        Impact,
        Likelihood,
        AffectedServices.Select(s => s.ServiceKey),
        UtcTime.ToOffset(RequestedStartUtc),
        UtcTime.ToOffset(RequestedEndUtc),
        UtcTime.ToOffset(CreatedAtUtc),
        Description,
        ImplementationPlan,
        BackoutPlan,
        State,
        UtcTime.ToOffset(UpdatedAtUtc));

    /// <summary>Writes a domain aggregate back over this row.</summary>
    public void ApplyFrom(ChangeRequest change)
    {
        ArgumentNullException.ThrowIfNull(change);

        Reference = change.Reference.ToString();
        ReferenceYear = change.Reference.Year;
        ReferenceSequence = change.Reference.Sequence;
        Title = change.Title;
        RequestedBy = change.RequestedBy;
        Description = change.Description;
        ImplementationPlan = change.ImplementationPlan;
        BackoutPlan = change.BackoutPlan;
        Type = change.Type;
        Impact = change.Impact;
        Likelihood = change.Likelihood;
        RequestedStartUtc = UtcTime.FromOffset(change.RequestedStartUtc);
        RequestedEndUtc = UtcTime.FromOffset(change.RequestedEndUtc);
        State = change.State;
        CreatedAtUtc = UtcTime.FromOffset(change.CreatedAtUtc);
        UpdatedAtUtc = UtcTime.FromOffset(change.UpdatedAtUtc);

        var desired = change.AffectedServiceKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var existing in AffectedServices.ToList())
        {
            if (!desired.Remove(existing.ServiceKey))
            {
                AffectedServices.Remove(existing);
            }
        }

        foreach (var key in desired)
        {
            AffectedServices.Add(new ChangeAffectedServiceRecord { ServiceKey = key });
        }
    }
}

public sealed class ChangeAffectedServiceRecord
{
    public int Id { get; set; }

    public int ChangeRequestId { get; set; }

    public string ServiceKey { get; set; } = string.Empty;
}
