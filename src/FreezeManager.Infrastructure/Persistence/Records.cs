using FreezeManager.Domain.Calendar;
using FreezeManager.Domain.Approvals;
using FreezeManager.Domain.Audit;
using FreezeManager.Domain.Changes;
using FreezeManager.Domain.Overrides;
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

    /// <summary>
    /// Rounds ingestion dropped, as JSON. Stored rather than only returned so that "why is round N
    /// missing?" can be answered later, not only in the second after a sync.
    /// </summary>
    public string? SkippedRoundsJson { get; set; }
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

    public List<ChangeApprovalRecord> Approvals { get; set; } = new();

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

    public ChangeRequest ToDomainWithApprovals()
    {
        var change = ToDomain();
        change.RehydrateApprovals(Approvals.Select(a => a.ToDomain()));
        return change;
    }

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

        // Approvals are add-only here. Clearing them is the aggregate's job when a change returns
        // to draft, and that clearing is reflected by the domain handing back an empty list.
        if (change.Approvals.Count == 0)
        {
            Approvals.Clear();
        }
        else
        {
            foreach (var approval in change.Approvals)
            {
                if (Approvals.Any(a => a.Role == approval.Role))
                {
                    continue;
                }

                Approvals.Add(new ChangeApprovalRecord
                {
                    Role = approval.Role,
                    Approver = approval.Approver,
                    Decision = approval.Decision,
                    DecidedAtUtc = UtcTime.FromOffset(approval.DecidedAtUtc),
                    Comment = approval.Comment
                });
            }
        }
    }
}

public sealed class ChangeAffectedServiceRecord
{
    public int Id { get; set; }

    public int ChangeRequestId { get; set; }

    public string ServiceKey { get; set; } = string.Empty;
}

public sealed class ChangeApprovalRecord
{
    public int Id { get; set; }

    public int ChangeRequestId { get; set; }

    public ApprovalRole Role { get; set; }

    public string Approver { get; set; } = string.Empty;

    public ApprovalDecision Decision { get; set; }

    public DateTime DecidedAtUtc { get; set; }

    public string? Comment { get; set; }

    public ChangeApproval ToDomain() =>
        new(Role, Approver, Decision, UtcTime.ToOffset(DecidedAtUtc), Comment);
}

public sealed class FreezeOverrideRecord
{
    public int Id { get; set; }

    public string ChangeReference { get; set; } = string.Empty;

    public string IncidentReference { get; set; } = string.Empty;

    public string Justification { get; set; } = string.Empty;

    public string RequestedBy { get; set; } = string.Empty;

    public DateTime RequestedAtUtc { get; set; }

    public int GrantDurationMinutes { get; set; }

    public bool IsBreakGlass { get; set; }

    public OverrideState State { get; set; }

    public DateTime? GrantedAtUtc { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime? UsedAtUtc { get; set; }

    public DateTime? RetrospectiveDueAtUtc { get; set; }

    public DateTime? RetrospectiveCompletedAtUtc { get; set; }

    public string? RetrospectiveNotes { get; set; }

    /// <summary>Set once the sweeper has written the expiry audit entry, so it writes exactly one.</summary>
    public bool ExpiryAudited { get; set; }

    /// <summary>Set once the overdue-retrospective audit entry has been written.</summary>
    public bool RetrospectiveOverdueAudited { get; set; }

    public List<OverrideApprovalRecord> Approvals { get; set; } = new();

    public FreezeOverride ToDomain() => FreezeOverride.Rehydrate(
        ChangeReference,
        IncidentReference,
        Justification,
        RequestedBy,
        UtcTime.ToOffset(RequestedAtUtc),
        TimeSpan.FromMinutes(GrantDurationMinutes),
        IsBreakGlass,
        State,
        UtcTime.ToOffset(GrantedAtUtc),
        UtcTime.ToOffset(ExpiresAtUtc),
        UtcTime.ToOffset(UsedAtUtc),
        UtcTime.ToOffset(RetrospectiveDueAtUtc),
        UtcTime.ToOffset(RetrospectiveCompletedAtUtc),
        RetrospectiveNotes,
        Approvals.Select(a => a.ToDomain()));

    public void ApplyFrom(FreezeOverride source)
    {
        ArgumentNullException.ThrowIfNull(source);

        ChangeReference = source.ChangeReference;
        IncidentReference = source.IncidentReference;
        Justification = source.Justification;
        RequestedBy = source.RequestedBy;
        RequestedAtUtc = UtcTime.FromOffset(source.RequestedAtUtc);
        GrantDurationMinutes = (int)source.GrantDuration.TotalMinutes;
        IsBreakGlass = source.IsBreakGlass;
        State = source.State;
        GrantedAtUtc = UtcTime.FromOffset(source.GrantedAtUtc);
        ExpiresAtUtc = UtcTime.FromOffset(source.ExpiresAtUtc);
        UsedAtUtc = UtcTime.FromOffset(source.UsedAtUtc);
        RetrospectiveDueAtUtc = UtcTime.FromOffset(source.RetrospectiveDueAtUtc);
        RetrospectiveCompletedAtUtc = UtcTime.FromOffset(source.RetrospectiveCompletedAtUtc);
        RetrospectiveNotes = source.RetrospectiveNotes;

        foreach (var approval in source.Approvals)
        {
            if (Approvals.Any(a => a.Role == approval.Role))
            {
                continue;
            }

            Approvals.Add(new OverrideApprovalRecord
            {
                Role = approval.Role,
                Approver = approval.Approver,
                Decision = approval.Decision,
                DecidedAtUtc = UtcTime.FromOffset(approval.DecidedAtUtc),
                Comment = approval.Comment
            });
        }
    }
}

public sealed class OverrideApprovalRecord
{
    public int Id { get; set; }

    public int FreezeOverrideId { get; set; }

    public ApprovalRole Role { get; set; }

    public string Approver { get; set; } = string.Empty;

    public ApprovalDecision Decision { get; set; }

    public DateTime DecidedAtUtc { get; set; }

    public string? Comment { get; set; }

    public ChangeApproval ToDomain() =>
        new(Role, Approver, Decision, UtcTime.ToOffset(DecidedAtUtc), Comment);
}

/// <summary>
/// One row of the audit log. Append-only: <see cref="AppendOnlyAuditInterceptor"/> refuses updates
/// and deletes, and each row's hash covers the row before it.
/// </summary>
public sealed class AuditEntryRecord
{
    public long Sequence { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string Actor { get; set; } = string.Empty;

    public AuditAction Action { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Details { get; set; } = "{}";

    public string PreviousHash { get; set; } = AuditEntry.GenesisHash;

    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Rebuilds the domain entry from the stored fields.
    /// </summary>
    /// <remarks>
    /// The constructor recomputes the hash from the contents, so a row whose stored hash was edited
    /// produces a domain entry whose hash differs -- which is exactly what verification compares.
    /// Use <see cref="StoredHash"/> for the value as written.
    /// </remarks>
    public AuditEntry ToDomain() => new(
        Sequence, UtcTime.ToOffset(OccurredAtUtc), Actor, Action, Subject, Details, PreviousHash);

    public string StoredHash => Hash;
}
