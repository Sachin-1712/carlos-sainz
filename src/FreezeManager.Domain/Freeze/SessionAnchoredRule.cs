using FreezeManager.Domain.Calendar;

namespace FreezeManager.Domain.Freeze;

/// <summary>
/// One window per session, rather than one spanning the whole weekend.
/// </summary>
/// <remarks>
/// This is what lets a lower-tier service stay deployable on the Friday evening between practice
/// sessions, instead of being locked out from Thursday to Sunday for no operational reason.
/// </remarks>
public sealed class SessionAnchoredRule : FreezeRule
{
    public SessionAnchoredRule(
        TimeSpan leadIn,
        TimeSpan leadOut,
        string description,
        IEnumerable<SessionType>? sessionTypes = null)
        : base(description)
    {
        if (leadIn < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leadIn), leadIn, "Lead-in is a positive duration before the session.");
        }

        if (leadOut < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leadOut), leadOut, "Lead-out is a positive duration after the session.");
        }

        LeadIn = leadIn;
        LeadOut = leadOut;
        SessionTypes = sessionTypes?.Distinct().ToArray();
    }

    /// <summary>How long before each session the freeze starts.</summary>
    public TimeSpan LeadIn { get; }

    /// <summary>How long after each session the freeze runs on.</summary>
    public TimeSpan LeadOut { get; }

    /// <summary>Sessions this rule applies to. Null means every session of the weekend.</summary>
    public IReadOnlyList<SessionType>? SessionTypes { get; }

    public override IEnumerable<FreezeWindow> WindowsFor(RaceEvent raceEvent, bool isAdvisory)
    {
        ArgumentNullException.ThrowIfNull(raceEvent);

        if (raceEvent.IsCancelled)
        {
            yield break;
        }

        foreach (var session in raceEvent.Sessions)
        {
            if (SessionTypes is not null && !SessionTypes.Contains(session.Type))
            {
                continue;
            }

            var startUtc = session.EffectiveStartUtc - LeadIn;
            var endUtc = session.EffectiveEndUtc + LeadOut;

            if (endUtc <= startUtc)
            {
                continue;
            }

            yield return new FreezeWindow(
                startUtc,
                endUtc,
                $"{Description} ({session.Type}, {raceEvent.Label})",
                isAdvisory,
                new[] { raceEvent.Round });
        }
    }
}
