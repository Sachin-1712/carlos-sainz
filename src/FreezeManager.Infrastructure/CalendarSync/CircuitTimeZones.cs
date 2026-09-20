namespace FreezeManager.Infrastructure.CalendarSync;

/// <summary>
/// Maps the upstream API's circuit identifiers to IANA time zones. The API does not publish zones.
/// A miss is loud (a warning at sync time) but not dangerous: freeze windows are instants and do
/// not depend on the zone; only the display does.
/// </summary>
public static class CircuitTimeZones
{
    /// <summary>The domain owns this value so the completeness check agrees with ingestion.</summary>
    public const string Unknown = FreezeManager.Domain.Calendar.RaceEvent.UnresolvedTimeZoneId;

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Current-era circuits, by the upstream API's circuit identifier.
        ["albert_park"] = "Australia/Melbourne",
        ["shanghai"] = "Asia/Shanghai",
        ["suzuka"] = "Asia/Tokyo",
        ["bahrain"] = "Asia/Bahrain",
        ["jeddah"] = "Asia/Riyadh",
        ["miami"] = "America/New_York",
        ["imola"] = "Europe/Rome",
        ["monaco"] = "Europe/Monaco",
        ["catalunya"] = "Europe/Madrid",
        ["villeneuve"] = "America/Toronto",
        ["red_bull_ring"] = "Europe/Vienna",
        ["silverstone"] = "Europe/London",
        ["spa"] = "Europe/Brussels",
        ["hungaroring"] = "Europe/Budapest",
        ["zandvoort"] = "Europe/Amsterdam",
        ["monza"] = "Europe/Rome",
        ["baku"] = "Asia/Baku",
        ["marina_bay"] = "Asia/Singapore",
        ["americas"] = "America/Chicago",
        ["rodriguez"] = "America/Mexico_City",
        ["interlagos"] = "America/Sao_Paulo",
        ["vegas"] = "America/Los_Angeles",
        ["losail"] = "Asia/Qatar",
        ["yas_marina"] = "Asia/Dubai",

        // Madrid, new for 2026. "madrid" is an alias in case the upstream identifier differs from
        // the circuit's name: both resolve to the same zone, so the alias can only help.
        ["madring"] = "Europe/Madrid",
        ["madrid"] = "Europe/Madrid",

        // Circuits that have rotated on and off the calendar in recent seasons. Cheap to carry, and
        // each one is a warning that never has to be chased.
        ["portimao"] = "Europe/Lisbon",
        ["mugello"] = "Europe/Rome",
        ["nurburgring"] = "Europe/Berlin",
        ["hockenheimring"] = "Europe/Berlin",
        ["istanbul"] = "Europe/Istanbul",
        ["ricard"] = "Europe/Paris",
        ["sochi"] = "Europe/Moscow",
        ["sepang"] = "Asia/Kuala_Lumpur",
        ["buddh"] = "Asia/Kolkata",
    };

    /// <summary>Every circuit identifier the table knows, for diagnostics and tests.</summary>
    public static IReadOnlyCollection<string> KnownCircuitIds => Map.Keys;

    public static bool TryResolve(string? circuitId, out string timeZoneId)
    {
        if (circuitId is not null && Map.TryGetValue(circuitId, out var found))
        {
            timeZoneId = found;
            return true;
        }

        timeZoneId = Unknown;
        return false;
    }
}
