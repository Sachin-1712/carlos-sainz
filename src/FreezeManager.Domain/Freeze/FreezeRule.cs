using FreezeManager.Domain.Calendar;

namespace FreezeManager.Domain.Freeze;

/// <summary>Produces the freeze windows a single rule contributes for one race event.</summary>
public abstract class FreezeRule
{
    protected FreezeRule(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description;
    }

    /// <summary>Shown to the engineer who was blocked, so it should read as a reason, not an id.</summary>
    public string Description { get; }

    /// <summary>
    /// Windows this rule contributes for the given event. Returns nothing when the event is
    /// cancelled, or when the rule's anchors cannot be resolved from the data on the event.
    /// </summary>
    public abstract IEnumerable<FreezeWindow> WindowsFor(RaceEvent raceEvent, bool isAdvisory);
}
