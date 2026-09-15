using FreezeManager.Domain.Calendar;
using FreezeManager.Domain.Services;

namespace FreezeManager.Domain.Freeze;

/// <summary>The freeze rules that apply to one service tier.</summary>
public sealed class FreezePolicy
{
    public FreezePolicy(ServiceTier tier, string name, IEnumerable<FreezeRule> rules, bool isAdvisory = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(rules);

        var ruleList = rules.ToArray();

        if (ruleList.Length == 0)
        {
            throw new ArgumentException($"Policy '{name}' must define at least one rule.", nameof(rules));
        }

        Tier = tier;
        Name = name;
        Rules = ruleList;
        IsAdvisory = isAdvisory;
    }

    public ServiceTier Tier { get; }

    public string Name { get; }

    /// <summary>
    /// When true this policy warns but never blocks. The distinction is the point: a process that
    /// blocks everything gets routed around, and a control that is routed around is worse than no
    /// control, because it also stops telling you the truth about what is being changed.
    /// </summary>
    public bool IsAdvisory { get; }

    public IReadOnlyList<FreezeRule> Rules { get; }

    public IReadOnlyList<FreezeWindow> WindowsFor(RaceEvent raceEvent)
    {
        ArgumentNullException.ThrowIfNull(raceEvent);
        return Rules.SelectMany(r => r.WindowsFor(raceEvent, IsAdvisory)).ToArray();
    }

    public override string ToString() => $"{Name} [{Tier}]{(IsAdvisory ? " advisory" : string.Empty)}";
}
