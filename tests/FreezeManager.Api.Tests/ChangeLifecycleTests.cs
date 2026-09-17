using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FreezeManager.Api.Tests.Support;
using Xunit;

namespace FreezeManager.Api.Tests;

/// <summary>
/// End to end over the real HTTP surface. FP1 is <see cref="FreezeApiFactory.Fp1"/>; the trackside
/// freeze runs FP1-48h to FP1+55h.
/// </summary>
public class ChangeLifecycleTests : IClassFixture<FreezeApiFactory>
{
    private static readonly DateTimeOffset T = FreezeApiFactory.Fp1;

    private readonly HttpClient _client;

    public ChangeLifecycleTests(FreezeApiFactory factory) => _client = factory.CreateClient();

    private async Task<string> CreateDraftAsync(DateTimeOffset start, TimeSpan duration, params string[] services)
    {
        var response = await _client.PostJsonAsync("/api/changes", ApiClient.CompleteDraft(start, duration, services: services));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.ReadJsonAsync()).GetProperty("reference").GetString()!;
    }

    [Fact]
    public async Task A_draft_is_created_with_a_reference_and_a_derived_risk()
    {
        var response = await _client.PostJsonAsync(
            "/api/changes", ApiClient.CompleteDraft(T.AddDays(-10), TimeSpan.FromHours(2)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.ReadJsonAsync();
        Assert.StartsWith("CHG-2026-", body.GetProperty("reference").GetString());
        Assert.Equal("Draft", body.GetProperty("state").GetString());
        Assert.True(body.GetProperty("isEditable").GetBoolean());

        // Medium impact against low likelihood is a low risk, computed server side.
        Assert.Equal("Low", body.GetProperty("risk").GetString());
        Assert.Equal(2, body.GetProperty("requestedDurationHours").GetDouble());
    }

    [Fact]
    public async Task References_are_allocated_in_sequence()
    {
        var first = await CreateDraftAsync(T.AddDays(-10), TimeSpan.FromHours(1));
        var second = await CreateDraftAsync(T.AddDays(-10), TimeSpan.FromHours(1));

        Assert.NotEqual(first, second);
        Assert.True(string.CompareOrdinal(first, second) < 0);
    }

    [Fact]
    public async Task A_request_missing_its_essentials_is_refused_before_anything_is_stored()
    {
        var response = await _client.PostJsonAsync("/api/changes", new { title = "", requestedBy = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task A_window_that_ends_before_it_starts_is_refused()
    {
        var response = await _client.PostJsonAsync("/api/changes", new
        {
            title = "Backwards",
            requestedBy = "s.sindhe",
            requestedStartUtc = T,
            requestedEndUtc = T.AddHours(-1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_incomplete_draft_cannot_be_submitted_and_is_told_what_is_missing()
    {
        var create = await _client.PostJsonAsync("/api/changes", new
        {
            title = "Half-written",
            requestedBy = "s.sindhe",
            affectedServiceKeys = new[] { "telemetry-ingest" },
            requestedStartUtc = T.AddDays(-10),
            requestedEndUtc = T.AddDays(-10).AddHours(2)
        });

        var reference = (await create.ReadJsonAsync()).GetProperty("reference").GetString();
        var submit = await _client.PostEmptyAsync($"/api/changes/{reference}/submit");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, submit.StatusCode);

        var errors = (await submit.ReadJsonAsync()).GetProperty("errors").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();

        Assert.Contains(errors, e => e.Contains("backout plan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_change_outside_the_freeze_goes_all_the_way_to_closed()
    {
        var reference = await CreateDraftAsync(T.AddDays(-10), TimeSpan.FromHours(2));

        foreach (var (verb, expected) in new[]
                 {
                     ("submit", "Submitted"), ("schedule", "Scheduled"), ("start", "Implementing"),
                     ("complete", "Implemented"), ("close", "Closed")
                 })
        {
            var response = await _client.PostEmptyAsync($"/api/changes/{reference}/{verb}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expected, (await response.ReadJsonAsync()).GetProperty("state").GetString());
        }

        var final = await _client.GetAsync($"/api/changes/{reference}");
        var body = await final.ReadJsonAsync();
        Assert.Equal("Closed", body.GetProperty("state").GetString());
        Assert.Empty(body.GetProperty("nextStates").EnumerateArray());
    }

    [Fact]
    public async Task An_illegal_transition_is_a_conflict_that_names_the_legal_moves()
    {
        var reference = await CreateDraftAsync(T.AddDays(-10), TimeSpan.FromHours(2));

        var response = await _client.PostEmptyAsync($"/api/changes/{reference}/start");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.ReadJsonAsync();
        Assert.Equal("Draft", body.GetProperty("currentState").GetString());
        Assert.Equal("Implementing", body.GetProperty("requestedState").GetString());

        var allowed = body.GetProperty("allowedNextStates").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Submitted", allowed);
        Assert.DoesNotContain("Implementing", allowed);
    }

    [Fact]
    public async Task Only_a_draft_can_be_deleted()
    {
        var reference = await CreateDraftAsync(T.AddDays(-10), TimeSpan.FromHours(2));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/changes/{reference}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/changes/{reference}")).StatusCode);

        var submitted = await CreateDraftAsync(T.AddDays(-10), TimeSpan.FromHours(2));
        await _client.PostEmptyAsync($"/api/changes/{submitted}/submit");

        var refused = await _client.DeleteAsync($"/api/changes/{submitted}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task A_submitted_change_cannot_be_edited_until_it_is_withdrawn()
    {
        var reference = await CreateDraftAsync(T.AddDays(-10), TimeSpan.FromHours(2));
        await _client.PostEmptyAsync($"/api/changes/{reference}/submit");

        var edit = ApiClient.CompleteDraft(T.AddDays(-10), TimeSpan.FromHours(3), "Revised title");

        var refused = await _client.PutAsJsonAsync($"/api/changes/{reference}", edit, ApiClient.Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);

        await _client.PostEmptyAsync($"/api/changes/{reference}/withdraw");

        var accepted = await _client.PutAsJsonAsync($"/api/changes/{reference}", edit, ApiClient.Json);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("Revised title", (await accepted.ReadJsonAsync()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Changes_can_be_filtered_by_state_and_by_affected_service()
    {
        await CreateDraftAsync(T.AddDays(-20), TimeSpan.FromHours(1), "mission-control");

        var byService = await _client.GetAsync("/api/changes?affectedService=mission-control");
        var results = (await byService.ReadJsonAsync()).EnumerateArray().ToArray();
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Contains(
            "mission-control",
            r.GetProperty("affectedServiceKeys").EnumerateArray().Select(k => k.GetString())));

        var drafts = await _client.GetAsync("/api/changes?state=Draft");
        Assert.Equal(HttpStatusCode.OK, drafts.StatusCode);

        var unknown = await _client.GetAsync("/api/changes?state=Nonsense");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task An_unknown_reference_is_a_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/changes/CHG-2026-9999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostEmptyAsync("/api/changes/CHG-2026-9999/submit")).StatusCode);
    }
}
