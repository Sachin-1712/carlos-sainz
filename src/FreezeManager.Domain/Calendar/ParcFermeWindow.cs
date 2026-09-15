namespace FreezeManager.Domain.Calendar;

/// <summary>
/// A period during which the cars are under parc ferme conditions.
/// </summary>
/// <remarks>
/// Stored as data on the event rather than derived in code. The shape of parc ferme on a sprint
/// weekend has been revised more than once as the sporting regulations changed -- if the rule lives
/// in a conditional, a regulation change becomes a code change and a release. Living in the
/// calendar, it becomes an edit by a duty manager. An event may carry zero, one, or several.
/// </remarks>
public sealed class ParcFermeWindow
{
    public ParcFermeWindow(DateTimeOffset startUtc, DateTimeOffset endUtc, string? label = null)
    {
        if (endUtc <= startUtc)
        {
            throw new ArgumentException(
                $"A parc ferme window must end after it starts (start: {startUtc:O}, end: {endUtc:O}).",
                nameof(endUtc));
        }

        StartUtc = startUtc.ToUniversalTime();
        EndUtc = endUtc.ToUniversalTime();
        Label = label;
    }

    public DateTimeOffset StartUtc { get; }

    public DateTimeOffset EndUtc { get; }

    /// <summary>Optional note, e.g. "sprint parc ferme" or "race parc ferme".</summary>
    public string? Label { get; }

    public TimeSpan Duration => EndUtc - StartUtc;

    public override string ToString() =>
        $"{Label ?? "Parc ferme"} [{StartUtc:yyyy-MM-dd HH:mm}Z .. {EndUtc:yyyy-MM-dd HH:mm}Z)";
}
