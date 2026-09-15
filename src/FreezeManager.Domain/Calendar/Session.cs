namespace FreezeManager.Domain.Calendar;

/// <summary>A single on-track session within a race event.</summary>
public sealed class Session
{
    public Session(
        SessionType type,
        DateTimeOffset scheduledStartUtc,
        DateTimeOffset scheduledEndUtc,
        DateTimeOffset? actualStartUtc = null,
        DateTimeOffset? actualEndUtc = null)
    {
        if (scheduledEndUtc <= scheduledStartUtc)
        {
            throw new ArgumentException(
                $"Session {type} must end after it starts (start: {scheduledStartUtc:O}, end: {scheduledEndUtc:O}).",
                nameof(scheduledEndUtc));
        }

        if (actualStartUtc.HasValue && actualEndUtc.HasValue && actualEndUtc.Value <= actualStartUtc.Value)
        {
            throw new ArgumentException(
                $"Session {type} actual end must follow its actual start.",
                nameof(actualEndUtc));
        }

        Type = type;
        ScheduledStartUtc = scheduledStartUtc.ToUniversalTime();
        ScheduledEndUtc = scheduledEndUtc.ToUniversalTime();
        ActualStartUtc = actualStartUtc?.ToUniversalTime();
        ActualEndUtc = actualEndUtc?.ToUniversalTime();
    }

    public SessionType Type { get; }

    public DateTimeOffset ScheduledStartUtc { get; }

    public DateTimeOffset ScheduledEndUtc { get; }

    /// <summary>Set once the session actually gets under way, which may not be when it was due to.</summary>
    public DateTimeOffset? ActualStartUtc { get; }

    /// <summary>Set once the session ends, which may be well after its scheduled end after red flags.</summary>
    public DateTimeOffset? ActualEndUtc { get; }

    public TimeSpan ScheduledDuration => ScheduledEndUtc - ScheduledStartUtc;

    /// <summary>Actual start if known, otherwise the scheduled one.</summary>
    public DateTimeOffset EffectiveStartUtc => ActualStartUtc ?? ScheduledStartUtc;

    /// <summary>
    /// Actual end if known. Otherwise, if the session has started late, the scheduled duration is
    /// projected forward from the actual start -- a session delayed forty minutes by a red flag
    /// drags its freeze window with it rather than thawing on the published timetable.
    /// </summary>
    public DateTimeOffset EffectiveEndUtc
    {
        get
        {
            if (ActualEndUtc.HasValue)
            {
                return ActualEndUtc.Value;
            }

            return ActualStartUtc.HasValue
                ? ActualStartUtc.Value + ScheduledDuration
                : ScheduledEndUtc;
        }
    }

    public bool HasStarted => ActualStartUtc.HasValue;

    public bool HasFinished => ActualEndUtc.HasValue;

    /// <summary>True when the session is running later than published, by any margin.</summary>
    public bool IsDelayed => ActualStartUtc.HasValue && ActualStartUtc.Value > ScheduledStartUtc;

    public override string ToString() => $"{Type} {EffectiveStartUtc:yyyy-MM-dd HH:mm}Z";
}
