using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FreezeManager.Api.Tests.Support;
using Xunit;

namespace FreezeManager.Api.Tests;

/// <summary>
/// The freeze gate over HTTP. The test calendar holds one weekend with FP1 at
/// <see cref="FreezeApiFactory.Fp1"/> (T), which puts the trackside freeze at T-48h .. T+55h.
/// </summary>
public class FreezeGateApiTests : IClassFixture<FreezeApiFactory>
{
    private static readonly DateTimeOffset T = FreezeApiFactory.Fp1;

    private readonly HttpClient _client;

    public FreezeGateApiTests(FreezeApiFactory factory) => _client = factory.CreateClient();

    private async Task<string> DraftAsync(DateTimeOffset start, TimeSpan duration, params string[] services)
    {
        var response = await _client.PostJsonAsync(
            "/api/changes", ApiClient.CompleteDraft(start, duration, services: services));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("reference").GetString()!;
    }

    [Fact]
    public async Task A_change_inside_the_freeze_is_refused_with_the_reason_and_the_way_forward()
    {
        var reference = await DraftAsync(T, TimeSpan.FromHours(4));
        var response = await _client.PostEmptyAsync($"/api/changes/{reference}/submit");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.ReadJsonAsync();
        Assert.Equal("Blocked by a change freeze", body.GetProperty("title").GetString());
        Assert.Equal(reference, body.GetProperty("reference").GetString());
        Assert.Equal("Draft", body.GetProperty("currentState").GetString());
        Assert.Equal("Submitted", body.GetProperty("requestedState").GetString());

        // The reason names the round, so the person reading it knows why.
        var conflicts = body.GetProperty("conflicts").EnumerateArray().ToArray();
        Assert.NotEmpty(conflicts);
        Assert.Contains("Round 1", conflicts[0].GetProperty("reason").GetString());
        Assert.False(conflicts[0].GetProperty("isAdvisory").GetBoolean());

        // And the refusal carries the next window that actually fits.
        var suggested = body.GetProperty("suggestedWindow");
        Assert.Equal(T.AddHours(55), suggested.GetProperty("startUtc").GetDateTimeOffset());
        Assert.True(suggested.GetProperty("durationHours").GetDouble() >= 4);
    }

    [Fact]
    public async Task The_suggested_retry_can_be_sent_straight_back_and_succeeds()
    {
        var reference = await DraftAsync(T, TimeSpan.FromHours(4));

        var blocked = await _client.PostEmptyAsync($"/api/changes/{reference}/submit");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var retry = (await blocked.ReadJsonAsync()).GetProperty("retryWith");
        Assert.Equal("POST", retry.GetProperty("method").GetString());
        Assert.Equal($"/api/changes/{reference}/submit", retry.GetProperty("path").GetString());

        // Replay it verbatim. The compliant path is one copy away.
        var body = JsonSerializer.Deserialize<JsonElement>(retry.GetProperty("body").GetRawText());
        var accepted = await _client.PostAsJsonAsync(retry.GetProperty("path").GetString()!, body, ApiClient.Json);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var result = await accepted.ReadJsonAsync();
        Assert.Equal("Submitted", result.GetProperty("state").GetString());
        Assert.Equal(T.AddHours(55), result.GetProperty("requestedStartUtc").GetDateTimeOffset());
        Assert.Equal("Allowed", result.GetProperty("freezeGate").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task The_suggestion_keeps_the_changes_own_duration_rather_than_filling_the_gap()
    {
        var reference = await DraftAsync(T, TimeSpan.FromHours(4));
        var blocked = await _client.PostEmptyAsync($"/api/changes/{reference}/submit");

        var body = await blocked.ReadJsonAsync();
        var retryBody = body.GetProperty("retryWith").GetProperty("body");

        var start = retryBody.GetProperty("requestedStartUtc").GetDateTimeOffset();
        var end = retryBody.GetProperty("requestedEndUtc").GetDateTimeOffset();

        Assert.Equal(TimeSpan.FromHours(4), end - start);

        // The open window itself is far longer than four hours.
        Assert.True(body.GetProperty("suggestedWindow").GetProperty("durationHours").GetDouble() > 100);
    }

    [Fact]
    public async Task Scheduling_is_gated_too_not_only_submission()
    {
        // Submit in the clear, then try to move the window into the freeze.
        var reference = await DraftAsync(T.AddDays(-10), TimeSpan.FromHours(2));
        Assert.Equal(HttpStatusCode.OK, (await _client.PostEmptyAsync($"/api/changes/{reference}/submit")).StatusCode);

        var response = await _client.PostJsonAsync(
            $"/api/changes/{reference}/schedule",
            new { requestedStartUtc = T.AddHours(6), requestedEndUtc = T.AddHours(8) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.ReadJsonAsync();
        Assert.Equal("Scheduled", body.GetProperty("requestedState").GetString());
        Assert.Equal($"/api/changes/{reference}/schedule", body.GetProperty("retryWith").GetProperty("path").GetString());
    }

    [Fact]
    public async Task A_refused_change_is_left_exactly_as_it_was()
    {
        var reference = await DraftAsync(T, TimeSpan.FromHours(4));
        await _client.PostEmptyAsync($"/api/changes/{reference}/submit");

        var after = await (await _client.GetAsync($"/api/changes/{reference}")).ReadJsonAsync();

        Assert.Equal("Draft", after.GetProperty("state").GetString());
        Assert.Equal(T, after.GetProperty("requestedStartUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_corporate_only_change_during_a_session_is_allowed_with_a_warning()
    {
        // T+49h is mid-race. Corporate is advisory, so it proceeds and is told why not to.
        var reference = await DraftAsync(T.AddHours(49), TimeSpan.FromMinutes(30), "intranet");
        var response = await _client.PostEmptyAsync($"/api/changes/{reference}/submit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var gate = (await response.ReadJsonAsync()).GetProperty("freezeGate");
        Assert.Equal("AllowedWithWarning", gate.GetProperty("outcome").GetString());
        Assert.NotEmpty(gate.GetProperty("conflicts").EnumerateArray());
        Assert.All(gate.GetProperty("conflicts").EnumerateArray(), c => Assert.True(c.GetProperty("isAdvisory").GetBoolean()));
    }

    [Fact]
    public async Task A_change_across_tiers_is_judged_by_the_strictest_one()
    {
        // At T+54h race support has thawed; trackside has not.
        var raceSupport = await DraftAsync(T.AddHours(54), TimeSpan.FromMinutes(30), "mission-control");
        Assert.Equal(HttpStatusCode.OK, (await _client.PostEmptyAsync($"/api/changes/{raceSupport}/submit")).StatusCode);

        var both = await DraftAsync(T.AddHours(54), TimeSpan.FromMinutes(30), "mission-control", "telemetry-ingest");
        var response = await _client.PostEmptyAsync($"/api/changes/{both}/submit");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var tiers = (await response.ReadJsonAsync()).GetProperty("tiers").EnumerateArray()
            .Select(t => t.GetString()).ToArray();

        Assert.Contains("Trackside", tiers);
        Assert.Contains("RaceSupport", tiers);
    }

    [Fact]
    public async Task An_unknown_service_fails_closed()
    {
        var reference = await DraftAsync(T.AddDays(-10), TimeSpan.FromHours(1), "telemetry-ingest", "does-not-exist");
        var response = await _client.PostEmptyAsync($"/api/changes/{reference}/submit");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var errors = (await response.ReadJsonAsync()).GetProperty("errors").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();

        Assert.Contains(errors, e => e.Contains("does-not-exist"));
    }

    [Fact]
    public async Task An_emergency_change_is_gated_like_any_other_in_this_phase()
    {
        var create = await _client.PostJsonAsync("/api/changes", new
        {
            title = "Emergency patch",
            requestedBy = "s.sindhe",
            description = "Live incident INC-1234.",
            implementationPlan = "Restart the affected node.",
            backoutPlan = "Fail over to the standby.",
            type = "Emergency",
            impact = "High",
            likelihood = "High",
            affectedServiceKeys = new[] { "telemetry-ingest" },
            requestedStartUtc = T.AddHours(2),
            requestedEndUtc = T.AddHours(3)
        });

        var body = await create.ReadJsonAsync();
        Assert.Equal("Critical", body.GetProperty("risk").GetString());

        var reference = body.GetProperty("reference").GetString();
        var submit = await _client.PostEmptyAsync($"/api/changes/{reference}/submit");

        // No override path until the approval chain exists.
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
        Assert.False((await submit.ReadJsonAsync()).TryGetProperty("override", out _));
    }
}
