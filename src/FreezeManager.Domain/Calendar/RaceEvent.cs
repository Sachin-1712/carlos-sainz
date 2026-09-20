namespace FreezeManager.Domain.Calendar;

/// <summary>A single round of the championship, with its sessions and parc ferme windows.</summary>
public sealed class RaceEvent
{
    /// <summary>
    /// What a circuit's time zone is set to when it could not be resolved. Defined here so the
    /// ingestion layer and the completeness check agree on one value.
    /// </summary>
    public const string UnresolvedTimeZoneId = "Etc/UTC";

    public RaceEvent(
        int round,
        string officialName,
        string circuit,
        string country,
        string localTimeZoneId,
        EventFormat format,
        IEnumerable<Session> sessions,
        IEnumerable<ParcFermeWindow>? parcFermeWindows = null,
        EventStatus status = EventStatus.Scheduled)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(round, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(officialName);
        ArgumentException.ThrowIfNullOrWhiteSpace(circuit);
        ArgumentException.ThrowIfNullOrWhiteSpace(country);
        ArgumentException.ThrowIfNullOrWhiteSpace(localTimeZoneId);
        ArgumentNullException.ThrowIfNull(sessions);

        var orderedSessions = sessions.OrderBy(s => s.EffectiveStartUtc).ToArray();

        if (orderedSessions.Length == 0 && status != EventStatus.Cancelled)
        {
            throw new ArgumentException(
                $"Round {round} ({officialName}) must have at least one session unless it is cancelled.",
                nameof(sessions));
        }

        var duplicateTypes = orderedSessions
            .GroupBy(s => s.Type)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.ToString())
            .ToArray();

        if (duplicateTypes.Length > 0)
        {
            throw new ArgumentException(
                $"Round {round} lists the same session more than once: {string.Join(", ", duplicateTypes)}.",
                nameof(sessions));
        }

        Round = round;
        OfficialName = officialName;
        Circuit = circuit;
        Country = country;
        LocalTimeZoneId = localTimeZoneId;
        Format = format;
        Status = status;
        Sessions = orderedSessions;
        ParcFermeWindows = (parcFermeWindows ?? Array.Empty<ParcFermeWindow>())
            .OrderBy(w => w.StartUtc)
            .ToArray();
    }

    public int Round { get; }

    public string OfficialName { get; }

    public string Circuit { get; }

    public string Country { get; }

    /// <summary>
    /// IANA time zone identifier for the circuit, e.g. "Australia/Melbourne". Held as a string
    /// rather than a <see cref="TimeZoneInfo"/> so the domain stays serialisable and free of the
    /// Windows/IANA identifier split; resolution to a concrete zone is a presentation concern.
    /// </summary>
    public string LocalTimeZoneId { get; }

    public EventFormat Format { get; }

    public EventStatus Status { get; }

    /// <summary>Sessions in chronological order.</summary>
    public IReadOnlyList<Session> Sessions { get; }

    /// <summary>Parc ferme windows in chronological order. May be empty.</summary>
    public IReadOnlyList<ParcFermeWindow> ParcFermeWindows { get; }

    public bool IsCancelled => Status == EventStatus.Cancelled;

    public bool IsSprintWeekend => Format == EventFormat.Sprint;

    public string Label => $"Round {Round} ({Circuit})";

    public DateTimeOffset? FirstSessionStartUtc =>
        Sessions.Count == 0 ? null : (DateTimeOffset?)Sessions.Min(s => s.EffectiveStartUtc);

    public DateTimeOffset? LastSessionEndUtc =>
        Sessions.Count == 0 ? null : (DateTimeOffset?)Sessions.Max(s => s.EffectiveEndUtc);

    public Session? RaceSession => Sessions.FirstOrDefault(s => s.Type == SessionType.Race);

    public DateTimeOffset? ParcFermeStartUtc =>
        ParcFermeWindows.Count == 0 ? null : (DateTimeOffset?)ParcFermeWindows.Min(w => w.StartUtc);

    /// <summary>The instant the cars are released from the last parc ferme window of the weekend.</summary>
    public DateTimeOffset? ParcFermeReleaseUtc =>
        ParcFermeWindows.Count == 0 ? null : (DateTimeOffset?)ParcFermeWindows.Max(w => w.EndUtc);

    public Session? SessionOfType(SessionType type) => Sessions.FirstOrDefault(s => s.Type == type);

    public override string ToString() => $"{Label} {OfficialName}";
}
