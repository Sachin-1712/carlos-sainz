using FreezeManager.Infrastructure.CalendarSync;
using FreezeManager.Infrastructure.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace FreezeManager.Infrastructure.Tests.CalendarSync;

/// <summary>
/// Calls the real API. Skipped unless FREEZE_LIVE_TESTS=1. Run it from a machine that can reach
/// api.jolpi.ca to confirm the provider works against the live service:
/// <code>FREEZE_LIVE_TESTS=1 dotnet test --filter LiveJolpica</code>
/// </summary>
public class LiveJolpicaTests
{
    private readonly ITestOutputHelper _output;

    public LiveJolpicaTests(ITestOutputHelper output) => _output = output;

    [LiveFact]
    public async Task The_live_api_returns_a_season_the_mapper_can_read()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var provider = new JolpicaCalendarProvider(http);

        var season = DateTime.UtcNow.Year;
        var result = await provider.FetchSeasonAsync(season);

        _output.WriteLine($"{result.ProviderName}: {result.Events.Count} events for {season}");
        foreach (var warning in result.Warnings)
        {
            _output.WriteLine($"  warning: {warning}");
        }

        Assert.NotEmpty(result.Events);
        Assert.All(result.Events, e => Assert.NotNull(e.ToDomain().RaceSession));
    }
}
