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

    [Fact]
    public async Task A_complete_calendar_reports_complete()
    {
        var response = await _client.GetAsync("/api/calendar/2026/completeness");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.ReadJsonAsync();
        Assert.True(body.GetProperty("isComplete").GetBoolean());
        Assert.Empty(body.GetProperty("missingRounds").EnumerateArray());
    }

    [Fact]
    public async Task Asking_for_more_rounds_than_are_loaded_reports_the_gap_as_a_conflict()
    {
        // A calendar with a hole in it is a finding, not a healthy 200: every missing round is a
        // race weekend the engine currently believes is open.
        var response = await _client.GetAsync("/api/calendar/2026/completeness?expectedRounds=24");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.ReadJsonAsync();
        Assert.False(body.GetProperty("isComplete").GetBoolean());

        var missing = body.GetProperty("missingRounds").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        Assert.Contains(24, missing);

        // And each one says which of the two causes applies, because they need different fixes.
        var causes = body.GetProperty("missingRoundCauses").EnumerateArray().ToArray();
        Assert.All(causes, c => Assert.Contains(
            c.GetProperty("cause").GetString(),
            new[] { "DroppedByIngestion", "NotSentByUpstream" }));
    }

    [Fact]
    public async Task The_sync_response_names_dropped_rounds_and_unmapped_circuits_as_fields()
    {
        var sync = await _client.PostEmptyAsync("/api/calendar/2026/sync");
        var body = await sync.ReadJsonAsync();

        // Both are things an operator has to act on, so neither is buried in warning prose.
        Assert.True(body.TryGetProperty("skippedRounds", out _));
        Assert.True(body.TryGetProperty("unmappedCircuitIds", out _));
    }
}
