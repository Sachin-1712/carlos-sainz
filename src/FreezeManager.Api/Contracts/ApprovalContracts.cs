using FreezeManager.Domain.Approvals;
using FreezeManager.Domain.Overrides;

namespace FreezeManager.Api.Contracts;

public sealed record ApprovalInput
{
    public ApprovalRole Role { get; init; }

    public string? Approver { get; init; }

    public ApprovalDecision Decision { get; init; } = ApprovalDecision.Approved;

    public string? Comment { get; init; }
}

public sealed record ApprovalResponse
{
    public required string Role { get; init; }

    public required string Approver { get; init; }

    public required string Decision { get; init; }

    public required DateTimeOffset DecidedAtUtc { get; init; }

    public string? Comment { get; init; }

    public static ApprovalResponse From(ChangeApproval approval) => new()
    {
        Role = approval.Role.ToString(),
        Approver = approval.Approver,
        Decision = approval.Decision.ToString(),
        DecidedAtUtc = approval.DecidedAtUtc,
        Comment = approval.Comment
    };
}

public sealed record ApprovalChainResponse
{
    public required string Rationale { get; init; }

    public required IReadOnlyList<string> RequiredRoles { get; init; }

    public required IReadOnlyList<string> OutstandingRoles { get; init; }

    public required IReadOnlyList<ApprovalResponse> Decisions { get; init; }

    public required bool IsSatisfied { get; init; }

    public required bool IsPreApproved { get; init; }

    public static ApprovalChainResponse From(ApprovalRequirement requirement, IReadOnlyList<ChangeApproval> approvals) => new()
    {
        Rationale = requirement.Rationale,
        RequiredRoles = requirement.Roles.Select(r => r.ToString()).ToArray(),
        OutstandingRoles = requirement.OutstandingRoles(approvals).Select(r => r.ToString()).ToArray(),
        Decisions = approvals.Select(ApprovalResponse.From).ToArray(),
        IsSatisfied = requirement.IsSatisfiedBy(approvals),
        IsPreApproved = requirement.IsPreApproved
    };
}

public sealed record OverrideRequestInput
{
    public string? IncidentReference { get; init; }

    public string? Justification { get; init; }

    public string? RequestedBy { get; init; }

    /// <summary>Defaults to two hours. Capped at twelve.</summary>
    public double? GrantDurationHours { get; init; }

    /// <summary>
    /// A single accountable approver instead of the full chain. Costs a mandatory retrospective
    /// within 24 hours.
    /// </summary>
    public bool BreakGlass { get; init; }
}

public sealed record RetrospectiveInput
{
    public string? CompletedBy { get; init; }

    public string? Notes { get; init; }
}

public sealed record OverrideResponse
{
    public required string ChangeReference { get; init; }

    public required string IncidentReference { get; init; }

    public required string Justification { get; init; }

    public required string RequestedBy { get; init; }

    public required DateTimeOffset RequestedAtUtc { get; init; }

    public required string State { get; init; }

    public required bool IsBreakGlass { get; init; }

    public required double GrantDurationHours { get; init; }

    public DateTimeOffset? GrantedAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public DateTimeOffset? UsedAtUtc { get; init; }

    public DateTimeOffset? RetrospectiveDueAtUtc { get; init; }

    public DateTimeOffset? RetrospectiveCompletedAtUtc { get; init; }

    public string? RetrospectiveNotes { get; init; }

    public required bool RetrospectiveOutstanding { get; init; }

    public required IReadOnlyList<ApprovalResponse> Approvals { get; init; }

    public IReadOnlyList<string>? OutstandingRoles { get; init; }

    public static OverrideResponse From(FreezeOverride source, IReadOnlyList<ApprovalRole>? outstanding = null) => new()
    {
        ChangeReference = source.ChangeReference,
        IncidentReference = source.IncidentReference,
        Justification = source.Justification,
        RequestedBy = source.RequestedBy,
        RequestedAtUtc = source.RequestedAtUtc,
        State = source.State.ToString(),
        IsBreakGlass = source.IsBreakGlass,
        GrantDurationHours = Math.Round(source.GrantDuration.TotalHours, 2),
        GrantedAtUtc = source.GrantedAtUtc,
        ExpiresAtUtc = source.ExpiresAtUtc,
        UsedAtUtc = source.UsedAtUtc,
        RetrospectiveDueAtUtc = source.RetrospectiveDueAtUtc,
        RetrospectiveCompletedAtUtc = source.RetrospectiveCompletedAtUtc,
        RetrospectiveNotes = source.RetrospectiveNotes,
        RetrospectiveOutstanding = source.RetrospectiveOutstanding,
        Approvals = source.Approvals.Select(ApprovalResponse.From).ToArray(),
        OutstandingRoles = outstanding is null || outstanding.Count == 0
            ? null
            : outstanding.Select(r => r.ToString()).ToArray()
    };
}
