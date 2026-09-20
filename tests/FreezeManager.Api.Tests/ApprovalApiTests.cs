using System.Net;
using FreezeManager.Api.Tests.Support;
using Xunit;

namespace FreezeManager.Api.Tests;

public class ApprovalApiTests : IClassFixture<FreezeApiFactory>
{
    private static readonly DateTimeOffset Clear = FreezeApiFactory.Fp1.AddDays(-10);

    private readonly HttpClient _client;

    public ApprovalApiTests(FreezeApiFactory factory) => _client = factory.CreateClient();

    private async Task<string> SubmittedAsync(string type = "Normal", params string[] services)
    {
        var draft = ApiClient.CompleteDraft(Clear, TimeSpan.FromHours(2), services: services);
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(draft, ApiClient.Json);
        var dictionary = payload.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        dictionary["type"] = type;

        var create = await _client.PostJsonAsync("/api/changes", dictionary);
        var reference = (await create.ReadJsonAsync()).GetProperty("reference").GetString()!;

        await _client.PostEmptyAsync($"/api/changes/{reference}/submit");
        return reference;
    }

    [Theory]
    [InlineData("intranet", 1, "ServiceOwner")]
    [InlineData("mission-control", 2, "HeadOfIt")]
    [InlineData("telemetry-ingest", 3, "RaceEngineeringNominee")]
    public async Task The_chain_grows_with_the_tier_the_change_touches(
        string serviceKey, int expectedRoles, string mustInclude)
    {
        var reference = await SubmittedAsync(services: serviceKey);

        var chain = await (await _client.GetAsync($"/api/changes/{reference}/approvals")).ReadJsonAsync();
        var roles = chain.GetProperty("requiredRoles").EnumerateArray().Select(r => r.GetString()).ToArray();

        Assert.Equal(expectedRoles, roles.Length);
        Assert.Contains(mustInclude, roles);
        Assert.False(chain.GetProperty("isSatisfied").GetBoolean());
    }

    [Fact]
    public async Task A_change_across_tiers_takes_the_strictest_chain()
    {
        var reference = await SubmittedAsync(services: ["intranet", "telemetry-ingest"]);

        var chain = await (await _client.GetAsync($"/api/changes/{reference}/approvals")).ReadJsonAsync();
        var roles = chain.GetProperty("requiredRoles").EnumerateArray().Select(r => r.GetString()).ToArray();

        Assert.Contains("RaceEngineeringNominee", roles);
        Assert.Contains("trackside", chain.GetProperty("rationale").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_standard_change_is_pre_approved_and_does_not_wait()
    {
        var reference = await SubmittedAsync("Standard", "telemetry-ingest");

        var change = await (await _client.GetAsync($"/api/changes/{reference}")).ReadJsonAsync();
        Assert.Equal("Approved", change.GetProperty("state").GetString());

        var chain = await (await _client.GetAsync($"/api/changes/{reference}/approvals")).ReadJsonAsync();
        Assert.True(chain.GetProperty("isPreApproved").GetBoolean());
        Assert.Empty(chain.GetProperty("requiredRoles").EnumerateArray());
    }

    [Fact]
    public async Task A_role_off_the_chain_cannot_approve()
    {
        var reference = await SubmittedAsync(services: "intranet");

        var response = await _client.PostJsonAsync($"/api/changes/{reference}/approvals",
            new { role = "RaceEngineeringNominee", approver = "r.nominee", decision = "Approved" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task The_change_advances_only_when_the_last_role_has_approved()
    {
        var reference = await SubmittedAsync(services: "mission-control");

        var first = await _client.PostJsonAsync($"/api/changes/{reference}/approvals",
            new { role = "ServiceOwner", approver = "s.owner", decision = "Approved" });
        Assert.Equal("Submitted", (await first.ReadJsonAsync()).GetProperty("state").GetString());

        var second = await _client.PostJsonAsync($"/api/changes/{reference}/approvals",
            new { role = "HeadOfIt", approver = "h.of.it", decision = "Approved" });
        Assert.Equal("Approved", (await second.ReadJsonAsync()).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_rejection_moves_the_change_to_rejected_and_it_cannot_proceed()
    {
        var reference = await SubmittedAsync(services: "intranet");

        var reject = await _client.PostJsonAsync($"/api/changes/{reference}/approvals",
            new { role = "ServiceOwner", approver = "s.owner", decision = "Rejected", comment = "No backout evidence." });

        Assert.Equal("Rejected", (await reject.ReadJsonAsync()).GetProperty("state").GetString());

        var schedule = await _client.PostEmptyAsync($"/api/changes/{reference}/schedule");
        Assert.Equal(HttpStatusCode.Conflict, schedule.StatusCode);
    }

    [Fact]
    public async Task A_change_cannot_be_scheduled_while_its_chain_is_incomplete()
    {
        var reference = await SubmittedAsync(services: "telemetry-ingest");

        var schedule = await _client.PostEmptyAsync($"/api/changes/{reference}/schedule");

        // Submitted cannot reach Scheduled at all now: approval sits between them.
        Assert.Equal(HttpStatusCode.Conflict, schedule.StatusCode);
        var allowed = (await schedule.ReadJsonAsync()).GetProperty("allowedNextStates")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Approved", allowed);
        Assert.DoesNotContain("Scheduled", allowed);
    }

    [Fact]
    public async Task Withdrawing_a_change_discards_its_approvals()
    {
        var reference = await SubmittedAsync(services: "mission-control");

        await _client.PostJsonAsync($"/api/changes/{reference}/approvals",
            new { role = "ServiceOwner", approver = "s.owner", decision = "Approved" });

        await _client.PostEmptyAsync($"/api/changes/{reference}/withdraw");
        await _client.PostEmptyAsync($"/api/changes/{reference}/submit");

        var chain = await (await _client.GetAsync($"/api/changes/{reference}/approvals")).ReadJsonAsync();

        Assert.Empty(chain.GetProperty("decisions").EnumerateArray());
        Assert.Equal(2, chain.GetProperty("outstandingRoles").GetArrayLength());
    }
}
