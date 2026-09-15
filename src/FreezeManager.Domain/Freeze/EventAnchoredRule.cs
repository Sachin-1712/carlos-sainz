using FreezeManager.Domain.Calendar;

namespace FreezeManager.Domain.Freeze;

/// <summary>
/// One window per race event, spanning from one anchor to another with offsets applied.
/// </summary>
/// <remarks>
/// Offsets are signed: pass a negative <see cref="TimeSpan"/> to start the freeze before its anchor.
/// </remarks>
public sealed class EventAnchoredRule : FreezeRule
{
    public EventAnchoredRule(
        FreezeAnchor startAnchor,
        TimeSpan startOffset,
        FreezeAnchor endAnchor,
        TimeSpan endOffset,
        string description,
        FreezeAnchor? endFallbackAnchor = null)
        : base(description)
    {
        StartAnchor = startAnchor;
        StartOffset = startOffset;
        EndAnchor = endAnchor;
        EndOffset = endOffset;
        EndFallbackAnchor = endFallbackAnchor;
    }

    public FreezeAnchor StartAnchor { get; }

    public TimeSpan StartOffset { get; }

    public FreezeAnchor EndAnchor { get; }

    public TimeSpan EndOffset { get; }

    /// <summary>
    /// Used when <see cref="EndAnchor"/> cannot be resolved. A policy anchored on parc ferme release
    /// still has to produce a window for an event whose parc ferme times have not been published
    /// yet; falling back to the last session end fails safe rather than silently opening the freeze.
    /// </summary>
    public FreezeAnchor? EndFallbackAnchor { get; }

    public override IEnumerable<FreezeWindow> WindowsFor(RaceEvent raceEvent, bool isAdvisory)
    {
        ArgumentNullException.ThrowIfNull(raceEvent);

        if (raceEvent.IsCancelled)
        {
            yield break;
        }

        var start = Resolve(raceEvent, StartAnchor);
        var end = Resolve(raceEvent, EndAnchor);

        if (end is null && EndFallbackAnchor.HasValue)
        {
            end = Resolve(raceEvent, EndFallbackAnchor.Value);
        }

        if (start is null || end is null)
        {
            yield break;
        }

        var startUtc = start.Value + StartOffset;
        var endUtc = end.Value + EndOffset;

        if (endUtc <= startUtc)
        {
            yield break;
        }

        yield return new FreezeWindow(
            startUtc,
            endUtc,
            $"{Description} ({raceEvent.Label})",
            isAdvisory,
            new[] { raceEvent.Round });
    }

    private static DateTimeOffset? Resolve(RaceEvent raceEvent, FreezeAnchor anchor) => anchor switch
    {
        FreezeAnchor.FirstSessionStart => raceEvent.FirstSessionStartUtc,
        FreezeAnchor.LastSessionEnd => raceEvent.LastSessionEndUtc,
        FreezeAnchor.RaceStart => raceEvent.RaceSession?.EffectiveStartUtc,
        FreezeAnchor.RaceEnd => raceEvent.RaceSession?.EffectiveEndUtc,
        FreezeAnchor.ParcFermeStart => raceEvent.ParcFermeStartUtc,
        FreezeAnchor.ParcFermeRelease => raceEvent.ParcFermeReleaseUtc,
        _ => null
    };
}
