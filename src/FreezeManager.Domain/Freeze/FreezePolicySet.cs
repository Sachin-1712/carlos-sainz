using FreezeManager.Domain.Services;

namespace FreezeManager.Domain.Freeze;

/// <summary>One freeze policy per service tier.</summary>
public sealed class FreezePolicySet
{
    private readonly Dictionary<ServiceTier, FreezePolicy> _byTier;

    public FreezePolicySet(IEnumerable<FreezePolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        _byTier = new Dictionary<ServiceTier, FreezePolicy>();

        foreach (var policy in policies)
        {
            if (!_byTier.TryAdd(policy.Tier, policy))
            {
                throw new ArgumentException(
                    $"More than one policy supplied for tier {policy.Tier}.",
                    nameof(policies));
            }
        }
    }

    public FreezePolicy? For(ServiceTier tier) => _byTier.GetValueOrDefault(tier);

    public IReadOnlyCollection<FreezePolicy> All => _byTier.Values;

    /// <summary>
    /// The default tiering. These are starting values, not constants: the whole point of expressing
    /// a policy as anchors and offsets is that a service owner can argue the numbers down without
    /// anyone touching the engine.
    /// </summary>
    public static FreezePolicySet Default { get; } = new(new[]
    {
        new FreezePolicy(
            ServiceTier.Trackside,
            "Trackside critical",
            new FreezeRule[]
            {
                // Freight is packed and the garage is built well before anyone drives. Ending two
                // hours past parc ferme release covers the tear-down, when the kit is still live.
                new EventAnchoredRule(
                    FreezeAnchor.FirstSessionStart,
                    TimeSpan.FromHours(-48),
                    FreezeAnchor.ParcFermeRelease,
                    TimeSpan.FromHours(2),
                    "Trackside freeze, load-in to tear-down",
                    endFallbackAnchor: FreezeAnchor.LastSessionEnd)
            }),

        new FreezePolicy(
            ServiceTier.RaceSupport,
            "Race support",
            new FreezeRule[]
            {
                // Mission control and the simulator are in use from the day before running starts,
                // and the debrief runs for hours after the flag.
                new EventAnchoredRule(
                    FreezeAnchor.FirstSessionStart,
                    TimeSpan.FromHours(-24),
                    FreezeAnchor.RaceEnd,
                    TimeSpan.FromHours(3),
                    "Race support freeze",
                    endFallbackAnchor: FreezeAnchor.LastSessionEnd)
            }),

        new FreezePolicy(
            ServiceTier.Corporate,
            "Corporate advisory",
            new FreezeRule[]
            {
                // Corporate IT is not in the critical path, so it gets a nudge around live running
                // and nothing more.
                new SessionAnchoredRule(
                    TimeSpan.FromMinutes(30),
                    TimeSpan.FromMinutes(30),
                    "Session in progress")
            },
            isAdvisory: true)
    });
}
