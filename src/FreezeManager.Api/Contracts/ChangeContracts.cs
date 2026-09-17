using FreezeManager.Domain.Changes;

namespace FreezeManager.Api.Contracts;

/// <summary>What a caller sends to create or replace a change.</summary>
public sealed record ChangeRequestInput
{
    public string? Title { get; init; }

    public string? RequestedBy { get; init; }

    public string? Description { get; init; }

    public string? ImplementationPlan { get; init; }

    public string? BackoutPlan { get; init; }

    public ChangeType Type { get; init; } = ChangeType.Normal;

    public ChangeImpact Impact { get; init; } = ChangeImpact.Medium;

    public ChangeLikelihood Likelihood { get; init; } = ChangeLikelihood.Medium;

    public IReadOnlyList<string>? AffectedServiceKeys { get; init; }

    public DateTimeOffset? RequestedStartUtc { get; init; }

    public DateTimeOffset? RequestedEndUtc { get; init; }
}

/// <summary>An optional replacement window sent with a submit or schedule call.</summary>
public sealed record ChangeWindowInput
{
    public DateTimeOffset? RequestedStartUtc { get; init; }

    public DateTimeOffset? RequestedEndUtc { get; init; }
}

public sealed record ChangeResponse
{
    public required string Reference { get; init; }

    public required string Title { get; init; }

    public required string RequestedBy { get; init; }

    public string? Description { get; init; }

    public string? ImplementationPlan { get; init; }

    public string? BackoutPlan { get; init; }

    public required string Type { get; init; }

    public required string Impact { get; init; }

    public required string Likelihood { get; init; }

    /// <summary>Derived from impact and likelihood; never sent in, only out.</summary>
    public required string Risk { get; init; }

    public required IReadOnlyList<string> AffectedServiceKeys { get; init; }

    public required DateTimeOffset RequestedStartUtc { get; init; }

    public required DateTimeOffset RequestedEndUtc { get; init; }

    public required double RequestedDurationHours { get; init; }

    public required string State { get; init; }

    /// <summary>Where this change may go next. Empty when it is finished.</summary>
    public required IReadOnlyList<string> NextStates { get; init; }

    public required bool IsEditable { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>Present when the freeze gate ran and allowed the move.</summary>
    public FreezeGateResponse? FreezeGate { get; init; }

    public static ChangeResponse From(ChangeRequest change, FreezeGateDecision? decision = null) => new()
    {
        Reference = change.Reference.ToString(),
        Title = change.Title,
        RequestedBy = change.RequestedBy,
        Description = change.Description,
        ImplementationPlan = change.ImplementationPlan,
        BackoutPlan = change.BackoutPlan,
        Type = change.Type.ToString(),
        Impact = change.Impact.ToString(),
        Likelihood = change.Likelihood.ToString(),
        Risk = change.Risk.ToString(),
        AffectedServiceKeys = change.AffectedServiceKeys,
        RequestedStartUtc = change.RequestedStartUtc,
        RequestedEndUtc = change.RequestedEndUtc,
        RequestedDurationHours = Math.Round(change.RequestedDuration.TotalHours, 2),
        State = change.State.ToString(),
        NextStates = change.NextStates.Select(s => s.ToString()).ToArray(),
        IsEditable = change.IsEditable,
        CreatedAtUtc = change.CreatedAtUtc,
        UpdatedAtUtc = change.UpdatedAtUtc,
        FreezeGate = decision is null ? null : FreezeGateResponse.From(decision)
    };
}
