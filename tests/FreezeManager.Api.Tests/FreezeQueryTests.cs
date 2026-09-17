using System.Net;
using FreezeManager.Api.Tests.Support;
using Xunit;

namespace FreezeManager.Api.Tests;

public class FreezeQueryTests : IClassFixture<FreezeApiFactory>
{
    private static readonly DateTimeOffset T = FreezeApiFactory.Fp1;

    private readonly HttpClient _client;

    public FreezeQueryTests(FreezeApiFactory factory) => _client = factory.CreateClient();

    /// <summary>
    /// A UTC instant safe to put in a query string. The round-trip "O" format on a DateTimeOffset
    /// ends in "+00:00", and a literal '+' in a query string decodes to a space.
    /// </summary>
    private static string Iso(DateTimeOffset instant) =>
        Uri.EscapeDataString(instant.UtcDateTime.ToString("O"));

    [Fact]
    public async Task Freeze_status_reports_frozen_during_the_weekend_and_how_long_is_left()
    {
        var response = await _client.GetAsync($"/api/freeze/status?serviceKey=telemetry-ingest&at={Iso(T)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.ReadJsonAsync();
        Assert.True(body.GetProperty("isFrozen").GetBoolean());
        Assert.Equal("Trackside", body.GetProperty("tier").GetString());
        Assert.Equal(55, body.GetProperty("hoursUntilThaw").GetDouble());
        Assert.Contains("Round 1", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Freeze_status_reports_open_before_the_weekend_and_when_the_freeze_lands()
    {
        var response = await _client.GetAsync($"/api/freeze/status?serviceKey=telemetry-ingest&at={Iso(T.AddHours(-72))}");

        var body = await response.ReadJsonAsync();
        Assert.False(body.GetProperty("isFrozen").GetBoolean());
        Assert.Equal(24, body.GetProperty("hoursUntilNextFreeze").GetDouble());
    }

    [Fact]
    public async Task Freeze_status_for_an_unknown_service_is_a_not_found()
    {
        var response = await _client.GetAsync("/api/freeze/status?serviceKey=nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_next_window_endpoint_answers_when_a_change_of_a_given_length_could_run()
    {
        var response = await _client.GetAsync($"/api/freeze/next-window?serviceKey=telemetry-ingest&hours=4&after={Iso(T)}");

        var window = (await response.ReadJsonAsync()).GetProperty("suggestedWindow");
        Assert.Equal(T.AddHours(55), window.GetProperty("startUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task Windows_for_a_service_are_returned_merged()
    {
        var response = await _client.GetAsync("/api/freeze/windows?serviceKey=telemetry-ingest");
        var windows = (await response.ReadJsonAsync()).EnumerateArray().ToArray();

        var window = Assert.Single(windows);
        Assert.Equal(T.AddHours(-48), window.GetProperty("startUtc").GetDateTimeOffset());
        Assert.Equal(T.AddHours(55), window.GetProperty("endUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task The_service_catalogue_lists_every_tier()
    {
        var response = await _client.GetAsync("/api/freeze/services");
        var services = (await response.ReadJsonAsync()).EnumerateArray().ToArray();

        Assert.Equal(3, services.Length);
        Assert.Equal("Trackside", services[0].GetProperty("tier").GetString());
    }

    [Fact]
    public async Task The_calendar_endpoint_exposes_each_events_provenance()
    {
        var response = await _client.GetAsync("/api/calendar/2026");
        var body = await response.ReadJsonAsync();

        var round = body.GetProperty("events").EnumerateArray().First();
        Assert.Equal("Seed", round.GetProperty("source").GetString());

        // The seeded weekend is not from a published source, and the API says so rather than hiding it.
        Assert.False(round.GetProperty("isVerified").GetBoolean());
    }

}
