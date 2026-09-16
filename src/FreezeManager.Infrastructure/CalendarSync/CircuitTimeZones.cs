namespace FreezeManager.Infrastructure.CalendarSync;

/// <summary>
/// Maps the upstream API's circuit identifiers to IANA time zones. The API does not publish zones.
/// A miss is loud (a warning at sync time) but not dangerous: freeze windows are instants and do
/// not depend on the zone; only the display does.
/// </summary>
public static class CircuitTimeZones
{
    public const string Unknown = "Etc/UTC";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
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
        ["yas_marina"] = "Asia/Dubai"
    };

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
