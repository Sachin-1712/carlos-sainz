namespace FreezeManager.Domain.Services;

/// <summary>An IT service that change requests are raised against.</summary>
public sealed class ManagedService
{
    public ManagedService(string key, string name, ServiceTier tier, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        Key = key;
        Name = name;
        Tier = tier;
        Owner = owner;
    }

    /// <summary>Stable identifier used in change records and the audit trail, e.g. "trackside-telemetry".</summary>
    public string Key { get; }

    public string Name { get; }

    public ServiceTier Tier { get; }

    /// <summary>Accountable service owner, who sits in the approval chain for this service.</summary>
    public string Owner { get; }

    public override string ToString() => $"{Name} ({Tier})";
}
