using System.Text.Json;
using FreezeManager.Infrastructure.CalendarSync.Ergast;

namespace FreezeManager.Infrastructure.Tests.Support;

internal static class Fixtures
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string Path(params string[] parts) =>
        System.IO.Path.Combine(new[] { AppContext.BaseDirectory }.Concat(parts).ToArray());

    public static string SeedCalendarPath => Path("Seed", "calendar-2026.json");

    public static string SeedServicesPath => Path("Seed", "services.json");

    public static ErgastResponse ErgastSample(bool withUnknownCircuit = false)
    {
        var json = File.ReadAllText(Path("Fixtures", "ergast-sample.json"));

        if (withUnknownCircuit)
        {
            json = json.Replace("\"madring\"", "\"not_a_real_circuit\"", StringComparison.Ordinal);
        }

        return JsonSerializer.Deserialize<ErgastResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Fixture did not deserialise.");
    }
}
