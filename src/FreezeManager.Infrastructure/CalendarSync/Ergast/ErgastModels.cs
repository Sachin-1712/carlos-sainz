using System.Text.Json.Serialization;

namespace FreezeManager.Infrastructure.CalendarSync.Ergast;

// The Ergast response shape, which Jolpica reproduces. Only the fields ingestion reads are
// modelled. Property names match the JSON; deserialisation is case-insensitive.

public sealed class ErgastResponse
{
    [JsonPropertyName("MRData")]
    public ErgastMrData? MRData { get; set; }
}

public sealed class ErgastMrData
{
    public string? Total { get; set; }

    public ErgastRaceTable? RaceTable { get; set; }
}

public sealed class ErgastRaceTable
{
    public string? Season { get; set; }

    public List<ErgastRace>? Races { get; set; }
}

public sealed class ErgastRace
{
    public string? Season { get; set; }

    public string? Round { get; set; }

    public string? RaceName { get; set; }

    public ErgastCircuit? Circuit { get; set; }

    /// <summary>Race date.</summary>
    public string? Date { get; set; }

    /// <summary>Race start time, UTC, when published.</summary>
    public string? Time { get; set; }

    public ErgastSessionTime? FirstPractice { get; set; }

    public ErgastSessionTime? SecondPractice { get; set; }

    public ErgastSessionTime? ThirdPractice { get; set; }

    public ErgastSessionTime? Qualifying { get; set; }

    public ErgastSessionTime? Sprint { get; set; }

    public ErgastSessionTime? SprintQualifying { get; set; }

    /// <summary>The 2023 name for sprint qualifying. Older data still uses it.</summary>
    public ErgastSessionTime? SprintShootout { get; set; }
}

public sealed class ErgastCircuit
{
    public string? CircuitId { get; set; }

    public string? CircuitName { get; set; }

    public ErgastLocation? Location { get; set; }
}

public sealed class ErgastLocation
{
    public string? Locality { get; set; }

    public string? Country { get; set; }
}

public sealed class ErgastSessionTime
{
    public string? Date { get; set; }

    public string? Time { get; set; }
}
