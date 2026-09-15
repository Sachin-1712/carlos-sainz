namespace FreezeManager.Domain.Calendar;

public enum EventStatus
{
    /// <summary>Times are as originally published.</summary>
    Scheduled = 0,

    /// <summary>Times have been amended -- weather, a rescheduled session, a revised calendar.</summary>
    Revised = 1,

    /// <summary>
    /// The event will not take place. Excluded from freeze calculation, but retained so that
    /// historic change records still resolve the round they referenced.
    /// </summary>
    Cancelled = 2
}
