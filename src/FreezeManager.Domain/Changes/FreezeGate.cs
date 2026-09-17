using FreezeManager.Domain.Freeze;
using FreezeManager.Domain.Services;

namespace FreezeManager.Domain.Changes;

/// <summary>
/// The control that stands between a change and a race weekend.
/// </summary>
/// <remarks>
/// The gate always answers with a way forward, never a bare refusal. A control that only says no
/// gets worked around; a control that says "not then, but here is when" gets used. That is why a
/// blocked decision carries the next window long enough for the change it just refused.
/// </remarks>
public sealed class FreezeGate
{
    private readonly FreezeCalculator _calculator;

    public FreezeGate(FreezeCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(calculator);
        _calculator = calculator;
    }

    /// <summary>
    /// Assesses a change against the tiers of every service it touches.
    /// </summary>
    /// <param name="change">The change being submitted or scheduled.</param>
    /// <param name="tiersByServiceKey">Tier of each affected service, by key.</param>
    public FreezeGateDecision Evaluate(
        ChangeRequest change,
        IReadOnlyDictionary<string, ServiceTier> tiersByServiceKey)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(tiersByServiceKey);

        var tiers = new List<ServiceTier>();
        var unknown = new List<string>();

        foreach (var key in change.AffectedServiceKeys)
        {
            if (tiersByServiceKey.TryGetValue(key, out var tier))
            {
                tiers.Add(tier);
            }
            else
            {
                unknown.Add(key);
            }
        }

        if (unknown.Count > 0)
        {
            // Failing closed: an unrecognised service is not evidence that a change is safe.
            return FreezeGateDecision.UnknownServices(unknown);
        }

        if (tiers.Count == 0)
        {
            return FreezeGateDecision.UnknownServices(new[] { "(no affected services)" });
        }

        var assessment = _calculator.AssessChangeWindow(
            tiers, change.RequestedStartUtc, change.RequestedEndUtc);

        return FreezeGateDecision.From(assessment);
    }

    /// <summary>The next window long enough for this change across every tier it touches.</summary>
    public OpenWindow? NextOpenWindowFor(
        ChangeRequest change,
        IReadOnlyDictionary<string, ServiceTier> tiersByServiceKey,
        DateTimeOffset afterUtc)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(tiersByServiceKey);

        var tiers = change.AffectedServiceKeys
            .Where(tiersByServiceKey.ContainsKey)
            .Select(k => tiersByServiceKey[k])
            .ToArray();

        return tiers.Length == 0
            ? null
            : _calculator.NextOpenWindow(tiers, afterUtc, change.RequestedDuration);
    }
}
