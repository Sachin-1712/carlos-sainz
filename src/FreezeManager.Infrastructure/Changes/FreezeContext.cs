using FreezeManager.Domain.Changes;
using FreezeManager.Domain.Freeze;
using FreezeManager.Domain.Services;
using FreezeManager.Infrastructure.Persistence;

namespace FreezeManager.Infrastructure.Changes;

/// <summary>
/// The stored calendar and service catalogue, assembled into a gate ready to answer questions.
/// </summary>
public sealed class FreezeContext
{
    public FreezeContext(FreezeCalculator calculator, IReadOnlyDictionary<string, ServiceTier> tiersByServiceKey)
    {
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(tiersByServiceKey);

        Calculator = calculator;
        TiersByServiceKey = tiersByServiceKey;
        Gate = new FreezeGate(calculator);
    }

    public FreezeCalculator Calculator { get; }

    public FreezeGate Gate { get; }

    public IReadOnlyDictionary<string, ServiceTier> TiersByServiceKey { get; }
}

/// <summary>Builds a <see cref="FreezeContext"/> from the store for a given season.</summary>
public sealed class FreezeContextFactory
{
    private readonly CalendarRepository _calendar;
    private readonly FreezePolicySet _policies;

    public FreezeContextFactory(CalendarRepository calendar, FreezePolicySet? policies = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        _calendar = calendar;
        _policies = policies ?? FreezePolicySet.Default;
    }

    public async Task<FreezeContext> CreateAsync(int season, CancellationToken cancellationToken = default)
    {
        var calendar = await _calendar.LoadSeasonAsync(season, cancellationToken);
        var services = await _calendar.LoadServicesAsync(cancellationToken);

        var tiers = services.ToDictionary(s => s.Key, s => s.Tier, StringComparer.OrdinalIgnoreCase);

        return new FreezeContext(new FreezeCalculator(calendar, _policies), tiers);
    }
}
