using FreezeManager.Domain.Changes;
using FreezeManager.Domain.Freeze;

namespace FreezeManager.Api.Contracts;

public sealed record FreezeWindowResponse
{
    public required DateTimeOffset StartUtc { get; init; }

    public required DateTimeOffset EndUtc { get; init; }

    public required string Reason { get; init; }

    public required bool IsAdvisory { get; init; }

    public required IReadOnlyList<int> Rounds { get; init; }

    public required double DurationHours { get; init; }

    public static FreezeWindowResponse From(FreezeWindow window) => new()
    {
        StartUtc = window.StartUtc,
        EndUtc = window.EndUtc,
        Reason = window.Reason,
        IsAdvisory = window.IsAdvisory,
        Rounds = window.Rounds,
        DurationHours = Math.Round(window.Duration.TotalHours, 2)
    };
}

public sealed record OpenWindowResponse
{
    public required DateTimeOffset StartUtc { get; init; }

    public required DateTimeOffset EndUtc { get; init; }

    public required double DurationHours { get; init; }

    public static OpenWindowResponse? From(OpenWindow? window) => window is null
        ? null
        : new OpenWindowResponse
        {
            StartUtc = window.StartUtc,
            EndUtc = window.EndUtc,
            DurationHours = Math.Round(window.Duration.TotalHours, 2)
        };
}

public sealed record FreezeGateResponse
{
    public required string Outcome { get; init; }

    public required IReadOnlyList<string> Tiers { get; init; }

    public required IReadOnlyList<FreezeWindowResponse> Conflicts { get; init; }

    public OpenWindowResponse? SuggestedWindow { get; init; }

    public static FreezeGateResponse From(FreezeGateDecision decision) => new()
    {
        Outcome = decision.Outcome.ToString(),
        Tiers = decision.Tiers.Select(t => t.ToString()).ToArray(),
        Conflicts = decision.Conflicts.Select(FreezeWindowResponse.From).ToArray(),
        SuggestedWindow = OpenWindowResponse.From(decision.SuggestedWindow)
    };
}

public sealed record FreezeStatusResponse
{
    public required string ServiceKey { get; init; }

    public required string Tier { get; init; }

    public required string Policy { get; init; }

    public required DateTimeOffset AtUtc { get; init; }

    public required bool IsFrozen { get; init; }

    public required bool IsAdvisory { get; init; }

    public string? Reason { get; init; }

    public FreezeWindowResponse? ActiveWindow { get; init; }

    public FreezeWindowResponse? NextWindow { get; init; }

    public double? HoursUntilThaw { get; init; }

    public double? HoursUntilNextFreeze { get; init; }
}
