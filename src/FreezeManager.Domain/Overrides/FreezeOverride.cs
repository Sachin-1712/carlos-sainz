using System.Globalization;
using FreezeManager.Domain.Approvals;

namespace FreezeManager.Domain.Overrides;

public enum OverrideState
{
    /// <summary>Raised, gathering approvals.</summary>
    Requested = 0,

    /// <summary>Approved and in force until it expires.</summary>
    Granted = 1,

    /// <summary>The change it was granted for went ahead.</summary>
    Used = 2,

    /// <summary>Reached its expiry without being used. Not renewable; ask again.</summary>
    Expired = 3,

    /// <summary>Withdrawn before expiry.</summary>
    Revoked = 4,

    /// <summary>The approval chain rejected it.</summary>
    Rejected = 5
}

/// <summary>
/// Permission for one change to proceed through a freeze, for a bounded period.
/// </summary>
/// <remarks>
/// Three properties make this a control rather than a bypass. It is attributable: a linked incident
/// and a written justification, both required. It is time-boxed: a grant expires on its own, and
/// the expiry is itself an audit event, so a standing exemption cannot accumulate. And it is
/// accountable: the approval chain is the same one the change's tier demands.
/// </remarks>
public sealed class FreezeOverride
{
    /// <summary>How long a grant lasts unless asked for otherwise.</summary>
    public static readonly TimeSpan DefaultGrantDuration = TimeSpan.FromHours(2);

    /// <summary>The longest a grant may last. Beyond this it is not an emergency, it is a plan.</summary>
    public static readonly TimeSpan MaximumGrantDuration = TimeSpan.FromHours(12);

    /// <summary>How long after a break-glass grant the retrospective is due.</summary>
    public static readonly TimeSpan RetrospectiveWindow = TimeSpan.FromHours(24);

    /// <summary>Shorter than this is not a justification, it is a shrug.</summary>
    public const int MinimumJustificationLength = 30;

    private readonly List<ChangeApproval> _approvals = new();

    public FreezeOverride(
        string changeReference,
        string incidentReference,
        string justification,
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        TimeSpan? grantDuration = null,
        bool isBreakGlass = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(changeReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);

        var problems = new List<string>();

        if (!IsIncidentReference(incidentReference))
        {
            problems.Add("A linked incident reference is required, in the form INC-1234.");
        }

        var trimmedJustification = justification?.Trim() ?? string.Empty;

        if (trimmedJustification.Length < MinimumJustificationLength)
        {
            problems.Add($"A justification of at least {MinimumJustificationLength} characters is required.");
        }

        var duration = grantDuration ?? DefaultGrantDuration;

        if (duration <= TimeSpan.Zero)
        {
            problems.Add("A grant must last a positive amount of time.");
        }
        else if (duration > MaximumGrantDuration)
        {
            problems.Add($"A grant cannot exceed {MaximumGrantDuration.TotalHours:0} hours.");
        }

        if (problems.Count > 0)
        {
            throw new OverrideValidationException(problems);
        }

        ChangeReference = changeReference;
        IncidentReference = incidentReference.Trim().ToUpperInvariant();
        Justification = trimmedJustification;
        RequestedBy = requestedBy.Trim();
        RequestedAtUtc = requestedAtUtc.ToUniversalTime();
        GrantDuration = duration;
        IsBreakGlass = isBreakGlass;
        State = OverrideState.Requested;
    }

    public string ChangeReference { get; }

    public string IncidentReference { get; }

    public string Justification { get; }

    public string RequestedBy { get; }

    public DateTimeOffset RequestedAtUtc { get; }

    public TimeSpan GrantDuration { get; }

    /// <summary>
    /// A single accountable approver instead of the full chain, for when the chain genuinely cannot
    /// be assembled. The cost is a mandatory retrospective within 24 hours.
    /// </summary>
    public bool IsBreakGlass { get; }

    public OverrideState State { get; private set; }

    public DateTimeOffset? GrantedAtUtc { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public DateTimeOffset? UsedAtUtc { get; private set; }

    public DateTimeOffset? RetrospectiveDueAtUtc { get; private set; }

    public DateTimeOffset? RetrospectiveCompletedAtUtc { get; private set; }

    public string? RetrospectiveNotes { get; private set; }

    public IReadOnlyList<ChangeApproval> Approvals => _approvals;

    public bool IsInForceAt(DateTimeOffset instant) =>
        State == OverrideState.Granted
        && ExpiresAtUtc is not null
        && instant >= GrantedAtUtc
        && instant < ExpiresAtUtc.Value;

    public bool RetrospectiveOutstanding =>
        IsBreakGlass && RetrospectiveDueAtUtc is not null && RetrospectiveCompletedAtUtc is null;

    public bool RetrospectiveOverdueAt(DateTimeOffset instant) =>
        RetrospectiveOutstanding && instant >= RetrospectiveDueAtUtc!.Value;

    /// <summary>The chain this override needs, given the change it belongs to.</summary>
    public IReadOnlyList<ApprovalRole> RequiredRoles(ApprovalRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        if (!IsBreakGlass)
        {
            // An override is never pre-approved, even for a standard change: going through a freeze
            // is the thing being approved, not the change itself.
            return requirement.IsPreApproved
                ? new[] { ApprovalRole.HeadOfIt }
                : requirement.Roles;
        }

        return ApprovalRequirement.BreakGlassRoles;
    }

    public void RecordApproval(ChangeApproval approval, ApprovalRequirement requirement, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(requirement);

        if (State != OverrideState.Requested)
        {
            throw new OverrideValidationException(
                new[] { $"An override in state {State} cannot take further approvals." });
        }

        var required = RequiredRoles(requirement);

        if (!required.Contains(approval.Role))
        {
            throw new OverrideValidationException(
                new[] { $"{approval.Role} is not on the approval chain for this override ({string.Join(", ", required)})." });
        }

        if (_approvals.Any(a => a.Role == approval.Role))
        {
            throw new OverrideValidationException(
                new[] { $"{approval.Role} has already recorded a decision on this override." });
        }

        _approvals.Add(approval);

        if (approval.Decision == ApprovalDecision.Rejected)
        {
            State = OverrideState.Rejected;
            return;
        }

        // Break-glass needs one approver from the privileged set; anything else needs all of them.
        var satisfied = IsBreakGlass
            ? _approvals.Any(a => a.IsApproval && ApprovalRequirement.BreakGlassRoles.Contains(a.Role))
            : required.All(role => _approvals.Any(a => a.IsApproval && a.Role == role));

        if (satisfied)
        {
            Grant(now);
        }
    }

    private void Grant(DateTimeOffset now)
    {
        State = OverrideState.Granted;
        GrantedAtUtc = now.ToUniversalTime();
        ExpiresAtUtc = GrantedAtUtc.Value + GrantDuration;

        if (IsBreakGlass)
        {
            RetrospectiveDueAtUtc = GrantedAtUtc.Value + RetrospectiveWindow;
        }
    }

    public void MarkUsed(DateTimeOffset now)
    {
        if (!IsInForceAt(now))
        {
            throw new OverrideValidationException(
                new[] { $"An override in state {State} is not in force and cannot be used." });
        }

        State = OverrideState.Used;
        UsedAtUtc = now.ToUniversalTime();
    }

    /// <summary>Called by the sweeper. Returns true when this call is what expired it.</summary>
    public bool ExpireIfElapsed(DateTimeOffset now)
    {
        if (State != OverrideState.Granted || ExpiresAtUtc is null || now < ExpiresAtUtc.Value)
        {
            return false;
        }

        State = OverrideState.Expired;
        return true;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (State is not (OverrideState.Requested or OverrideState.Granted))
        {
            throw new OverrideValidationException(
                new[] { $"An override in state {State} cannot be revoked." });
        }

        State = OverrideState.Revoked;
    }

    public void CompleteRetrospective(string notes, DateTimeOffset now)
    {
        if (!IsBreakGlass)
        {
            throw new OverrideValidationException(
                new[] { "Only a break-glass override has a retrospective." });
        }

        if (RetrospectiveCompletedAtUtc is not null)
        {
            throw new OverrideValidationException(new[] { "The retrospective is already complete." });
        }

        var trimmed = notes?.Trim() ?? string.Empty;

        if (trimmed.Length < MinimumJustificationLength)
        {
            throw new OverrideValidationException(
                new[] { $"Retrospective notes of at least {MinimumJustificationLength} characters are required." });
        }

        RetrospectiveNotes = trimmed;
        RetrospectiveCompletedAtUtc = now.ToUniversalTime();
    }

    /// <summary>Restores an override from storage without replaying its history.</summary>
    public static FreezeOverride Rehydrate(
        string changeReference,
        string incidentReference,
        string justification,
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        TimeSpan grantDuration,
        bool isBreakGlass,
        OverrideState state,
        DateTimeOffset? grantedAtUtc,
        DateTimeOffset? expiresAtUtc,
        DateTimeOffset? usedAtUtc,
        DateTimeOffset? retrospectiveDueAtUtc,
        DateTimeOffset? retrospectiveCompletedAtUtc,
        string? retrospectiveNotes,
        IEnumerable<ChangeApproval> approvals)
    {
        var restored = new FreezeOverride(
            changeReference, incidentReference, justification, requestedBy, requestedAtUtc,
            grantDuration, isBreakGlass)
        {
            State = state,
            GrantedAtUtc = grantedAtUtc?.ToUniversalTime(),
            ExpiresAtUtc = expiresAtUtc?.ToUniversalTime(),
            UsedAtUtc = usedAtUtc?.ToUniversalTime(),
            RetrospectiveDueAtUtc = retrospectiveDueAtUtc?.ToUniversalTime(),
            RetrospectiveCompletedAtUtc = retrospectiveCompletedAtUtc?.ToUniversalTime(),
            RetrospectiveNotes = retrospectiveNotes
        };

        restored._approvals.AddRange(approvals);
        return restored;
    }

    public static bool IsIncidentReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('-');

        return parts.Length == 2
            && parts[0].Equals("INC", StringComparison.OrdinalIgnoreCase)
            && parts[1].Length > 0
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number > 0;
    }

    public override string ToString() =>
        $"Override for {ChangeReference} [{State}]{(IsBreakGlass ? " break-glass" : string.Empty)} ({IncidentReference})";
}

public sealed class OverrideValidationException : InvalidOperationException
{
    public OverrideValidationException(IEnumerable<string> problems)
        : base(string.Join(" ", problems))
    {
        Problems = problems.ToArray();
    }

    public IReadOnlyList<string> Problems { get; }
}
