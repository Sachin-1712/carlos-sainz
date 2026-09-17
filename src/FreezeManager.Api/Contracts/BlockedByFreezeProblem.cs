using FreezeManager.Domain.Changes;

namespace FreezeManager.Api.Contracts;

/// <summary>
/// The body returned when the freeze gate blocks a change.
/// </summary>
/// <remarks>
/// It carries the next window that fits and a ready-made retry, because a refusal that does not say
/// what to do instead is the thing people route around. Taking the suggestion is copying
/// <see cref="RetryWith"/> into a second call.
/// </remarks>
public sealed record BlockedByFreezeProblem
{
    public const string ProblemType = "https://freeze-manager.invalid/problems/blocked-by-freeze";

    public string Type { get; init; } = ProblemType;

    public string Title { get; init; } = "Blocked by a change freeze";

    public int Status { get; init; } = StatusCodes.Status409Conflict;

    public required string Detail { get; init; }

    public required string Reference { get; init; }

    public required string CurrentState { get; init; }

    public required string RequestedState { get; init; }

    public required IReadOnlyList<string> Tiers { get; init; }

    public required IReadOnlyList<FreezeWindowResponse> Conflicts { get; init; }

    /// <summary>The next window long enough for this change. Null when none exists in the horizon.</summary>
    public OpenWindowResponse? SuggestedWindow { get; init; }

    /// <summary>A request that would succeed. Null when there is no window to suggest.</summary>
    public RetryInstruction? RetryWith { get; init; }

    public static BlockedByFreezeProblem From(
        ChangeRequest change,
        ChangeState requestedState,
        FreezeGateDecision decision,
        string path)
    {
        var suggested = OpenWindowResponse.From(decision.SuggestedWindow);

        return new BlockedByFreezeProblem
        {
            Detail = decision.ToString(),
            Reference = change.Reference.ToString(),
            CurrentState = change.State.ToString(),
            RequestedState = requestedState.ToString(),
            Tiers = decision.Tiers.Select(t => t.ToString()).ToArray(),
            Conflicts = decision.Conflicts.Select(FreezeWindowResponse.From).ToArray(),
            SuggestedWindow = suggested,
            RetryWith = suggested is null || decision.SuggestedWindow is null
                ? null
                : new RetryInstruction
                {
                    Method = "POST",
                    Path = path,
                    Body = new ChangeWindowInput
                    {
                        RequestedStartUtc = decision.SuggestedWindow.StartUtc,

                        // The suggested window can be far longer than the change needs; keep the
                        // change's own duration rather than expanding it to fill the gap.
                        RequestedEndUtc = decision.SuggestedWindow.StartUtc + change.RequestedDuration
                    }
                }
        };
    }
}

public sealed record RetryInstruction
{
    public required string Method { get; init; }

    public required string Path { get; init; }

    public required ChangeWindowInput Body { get; init; }
}
