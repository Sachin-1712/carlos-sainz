namespace FreezeManager.Domain.Calendar;

/// <summary>
/// Weekend format. Recorded for reporting and display only -- the freeze engine never branches on
/// it, because the sessions and parc ferme windows carried by the event already describe the shape
/// of the weekend. See <see cref="ParcFermeWindow"/> for why that matters.
/// </summary>
public enum EventFormat
{
    Conventional = 0,
    Sprint = 1
}
