namespace FreezeManager.Domain.Freeze;

/// <summary>A period in which a service may be changed.</summary>
public sealed class OpenWindow
{
    public OpenWindow(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (endUtc <= startUtc)
        {
            throw new ArgumentException(
                $"An open window must end after it starts (start: {startUtc:O}, end: {endUtc:O}).",
                nameof(endUtc));
        }

        StartUtc = startUtc.ToUniversalTime();
        EndUtc = endUtc.ToUniversalTime();
    }

    public DateTimeOffset StartUtc { get; }

    public DateTimeOffset EndUtc { get; }

    public TimeSpan Duration => EndUtc - StartUtc;

    public override string ToString() =>
        $"[{StartUtc:yyyy-MM-dd HH:mm}Z .. {EndUtc:yyyy-MM-dd HH:mm}Z) {Duration.TotalHours:F1}h";
}
