using System.Net;
using FreezeManager.Api.Tests.Support;
using Xunit;

namespace FreezeManager.Api.Tests;

/// <summary>
/// Kept apart from <see cref="FreezeQueryTests"/> on purpose: a sync adds a second weekend, and the
/// query tests assert against a calendar holding exactly one. Sharing a fixture would make those
/// tests pass or fail on execution order.
/// </summary>
public class CalendarSyncApiTests : IClassFixture<FreezeApiFactory>
{
    private readonly HttpClient _client;

    public CalendarSyncApiTests(FreezeApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task A_sync_reconciles_and_records_the_run()
    {
        var sync = await _client.PostEmptyAsync("/api/calendar/2026/sync");
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);

        var body = await sync.ReadJsonAsync();
        Assert.True(body.GetProperty("added").GetInt32() + body.GetProperty("updated").GetInt32() > 0);

        var runs = (await (await _client.GetAsync("/api/calendar/sync-runs")).ReadJsonAsync())
            .EnumerateArray().ToArray();

        Assert.NotEmpty(runs);
        Assert.True(runs[0].GetProperty("succeeded").GetBoolean());
    }
}
