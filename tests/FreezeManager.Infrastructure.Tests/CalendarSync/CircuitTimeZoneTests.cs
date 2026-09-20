using FreezeManager.Infrastructure.CalendarSync;
using Xunit;

namespace FreezeManager.Infrastructure.Tests.CalendarSync;

public class CircuitTimeZoneTests
{
    /// <summary>
    /// The circuits on the 2026 calendar. A miss here is a freeze window displayed in the wrong
    /// local time, which is exactly the class of bug the UTC-internally rule (decision 5) leaves
    /// to the presentation layer to get right.
    /// </summary>
    public static TheoryData<string> Circuits2026 =>
    [
        "albert_park", "shanghai", "suzuka", "bahrain", "jeddah", "miami", "villeneuve", "monaco",
        "catalunya", "red_bull_ring", "silverstone", "spa", "hungaroring", "zandvoort", "monza",
        "madring", "baku", "marina_bay", "americas", "rodriguez", "interlagos", "vegas", "losail",
        "yas_marina", "imola"
    ];

    [Theory]
    [MemberData(nameof(Circuits2026))]
    public void Every_circuit_on_the_calendar_resolves_to_a_real_time_zone(string circuitId)
    {
        Assert.True(CircuitTimeZones.TryResolve(circuitId, out var timeZoneId), $"no mapping for '{circuitId}'");
        Assert.NotEqual(CircuitTimeZones.Unknown, timeZoneId);

        // A mapping to a zone the runtime cannot load is worse than no mapping at all.
        Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
    }

    [Fact]
    public void Every_mapping_in_the_table_names_a_zone_the_runtime_can_load()
    {
        foreach (var circuitId in CircuitTimeZones.KnownCircuitIds)
        {
            Assert.True(CircuitTimeZones.TryResolve(circuitId, out var timeZoneId));
            Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        }
    }

    [Fact]
    public void An_unknown_circuit_resolves_to_utc_and_reports_that_it_was_not_found()
    {
        Assert.False(CircuitTimeZones.TryResolve("no_such_circuit", out var timeZoneId));
        Assert.Equal(CircuitTimeZones.Unknown, timeZoneId);
    }

    [Fact]
    public void Circuit_identifiers_are_matched_without_regard_to_case()
    {
        Assert.True(CircuitTimeZones.TryResolve("ALBERT_PARK", out var timeZoneId));
        Assert.Equal("Australia/Melbourne", timeZoneId);
    }
}
