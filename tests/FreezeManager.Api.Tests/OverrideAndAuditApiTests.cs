using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FreezeManager.Api.Tests.Support;
using Xunit;

namespace FreezeManager.Api.Tests;

/// <summary>
/// The emergency path end to end. FP1 is <see cref="FreezeApiFactory.Fp1"/> (T); trackside is
/// frozen T-48h to T+55h, and the fixed clock sits a month before that.
/// </summary>
public class OverrideAndAuditApiTests : IClassFixture<FreezeApiFactory>
{
    private static readonly DateTimeOffset T = FreezeApiFactory.Fp1;

    private const string Justification =
        "Telemetry ingest is dropping packets mid-session and the standby has already failed over.";

    private readonly HttpClient _client;

    public OverrideAndAuditApiTests(FreezeApiFactory factory) => _client = factory.CreateClient();

    private async Task<string> BlockedDraftAsync()
    {
        var create = await _client.PostJsonAsync(
            "/api/changes", ApiClient.CompleteDraft(T, TimeSpan.FromHours(2)));

        var reference = (await create.ReadJsonAsync()).GetProperty("reference").GetString()!;

        var submit = await _client.PostEmptyAsync($"/api/changes/{reference}/submit");
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);

        return reference;
    }

    private Task<HttpResponseMessage> RequestOverrideAsync(string reference, bool breakGlass = false) =>
        _client.PostJsonAsync($"/api/changes/{reference}/override", new
        {
            incidentReference = "INC-4471",
            justification = Justification,
            requestedBy = "s.sindhe",
            breakGlass
        });

    // ------------------------------------------------------------------ attributable

    [Fact]
    public async Task An_override_without_an_incident_reference_is_refused()
    {
        var reference = await BlockedDraftAsync();

        var response = await _client.PostJsonAsync($"/api/changes/{reference}/override", new
        {
            incidentReference = "",
            justification = Justification,
            requestedBy = "s.sindhe"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Contains(errors, e => e.Contains("incident", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_override_with_a_shrug_for_a_justification_is_refused()
    {
        var reference = await BlockedDraftAsync();

        var response = await _client.PostJsonAsync($"/api/changes/{reference}/override", new
        {
            incidentReference = "INC-4471",
            justification = "urgent",
            requestedBy = "s.sindhe"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_requested_override_names_the_chain_it_needs()
    {
        var reference = await BlockedDraftAsync();

        var response = await RequestOverrideAsync(reference);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.ReadJsonAsync();
        Assert.Equal("Requested", body.GetProperty("state").GetString());
        Assert.Equal("INC-4471", body.GetProperty("incidentReference").GetString());

        var outstanding = body.GetProperty("outstandingRoles").EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Equal(3, outstanding.Length);
        Assert.Contains("RaceEngineeringNominee", outstanding);
    }

    // ------------------------------------------------------------------ the whole emergency path

    [Fact]
    public async Task A_granted_override_lets_a_blocked_change_through_and_is_recorded_as_spent()
    {
        var reference = await BlockedDraftAsync();
        await RequestOverrideAsync(reference);

        foreach (var role in new[] { "TracksideItLead", "HeadOfIt", "RaceEngineeringNominee" })
        {
            var approve = await _client.PostJsonAsync(
                $"/api/changes/{reference}/override/approvals",
                new { role, approver = $"{role}.person", decision = "Approved" });

            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        }

        var granted = await (await _client.GetAsync($"/api/changes/{reference}/override")).ReadJsonAsync();
        Assert.Equal("Granted", granted.EnumerateArray().First().GetProperty("state").GetString());

        // The same submission that was refused now goes through.
        var submit = await _client.PostEmptyAsync($"/api/changes/{reference}/submit");
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);

        var result = await submit.ReadJsonAsync();
        Assert.Equal("Submitted", result.GetProperty("state").GetString());

        // The gate still says Blocked -- it did refuse. The response has to say plainly that an
        // override is what carried it, or a reader sees a success next to a refusal and guesses.
        Assert.Equal("Blocked", result.GetProperty("freezeGate").GetProperty("outcome").GetString());

        var via = result.GetProperty("proceededUnderOverride");
        Assert.Equal("INC-4471", via.GetProperty("incidentReference").GetString());
        Assert.False(via.GetProperty("breakGlass").GetBoolean());
        Assert.True(via.TryGetProperty("expiresAtUtc", out _));

        // And the override is spent, not left standing.
        var after = await (await _client.GetAsync($"/api/changes/{reference}/override")).ReadJsonAsync();
        Assert.Equal("Used", after.EnumerateArray().First().GetProperty("state").GetString());

        var audit = await AuditFor(reference);
        Assert.Contains("OverrideRequested", audit);
        Assert.Contains("OverrideGranted", audit);
        Assert.Contains("OverrideUsed", audit);
        Assert.Contains("ChangeBlockedByFreeze", audit);
    }

    [Fact]
    public async Task A_change_that_was_never_blocked_carries_no_override_marker()
    {
        var create = await _client.PostJsonAsync(
            "/api/changes", ApiClient.CompleteDraft(T.AddDays(-20), TimeSpan.FromHours(1)));
        var reference = (await create.ReadJsonAsync()).GetProperty("reference").GetString()!;

        var submit = await _client.PostEmptyAsync($"/api/changes/{reference}/submit");
        var body = await submit.ReadJsonAsync();

        Assert.Equal("Allowed", body.GetProperty("freezeGate").GetProperty("outcome").GetString());
        Assert.False(body.TryGetProperty("proceededUnderOverride", out _));
    }

    [Fact]
    public async Task A_partly_approved_override_does_not_let_anything_through()
    {
        var reference = await BlockedDraftAsync();
        await RequestOverrideAsync(reference);

        await _client.PostJsonAsync($"/api/changes/{reference}/override/approvals",
            new { role = "HeadOfIt", approver = "h.of.it", decision = "Approved" });

        var submit = await _client.PostEmptyAsync($"/api/changes/{reference}/submit");
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
    }

    [Fact]
    public async Task One_rejection_stops_the_override()
    {
        var reference = await BlockedDraftAsync();
        await RequestOverrideAsync(reference);

        await _client.PostJsonAsync($"/api/changes/{reference}/override/approvals",
            new { role = "TracksideItLead", approver = "t.lead", decision = "Approved" });

        var reject = await _client.PostJsonAsync($"/api/changes/{reference}/override/approvals",
            new { role = "HeadOfIt", approver = "h.of.it", decision = "Rejected", comment = "Wait for the gap." });

        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal("Rejected", (await reject.ReadJsonAsync()).GetProperty("state").GetString());

        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostEmptyAsync($"/api/changes/{reference}/submit")).StatusCode);
    }

    // ------------------------------------------------------------------ break-glass

    [Fact]
    public async Task Break_glass_needs_one_accountable_person_and_owes_a_retrospective()
    {
        var reference = await BlockedDraftAsync();

        var request = await RequestOverrideAsync(reference, breakGlass: true);
        Assert.Equal(HttpStatusCode.Created, request.StatusCode);

        var outstanding = (await request.ReadJsonAsync()).GetProperty("outstandingRoles")
            .EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Equal(2, outstanding.Length);
        Assert.DoesNotContain("RaceEngineeringNominee", outstanding);

        var approve = await _client.PostJsonAsync($"/api/changes/{reference}/override/approvals",
            new { role = "HeadOfIt", approver = "h.of.it", decision = "Approved" });

        var granted = await approve.ReadJsonAsync();
        Assert.Equal("Granted", granted.GetProperty("state").GetString());
        Assert.True(granted.GetProperty("isBreakGlass").GetBoolean());
        Assert.True(granted.GetProperty("retrospectiveOutstanding").GetBoolean());

        var due = granted.GetProperty("retrospectiveDueAtUtc").GetDateTimeOffset();
        var grantedAt = granted.GetProperty("grantedAtUtc").GetDateTimeOffset();
        Assert.Equal(TimeSpan.FromHours(24), due - grantedAt);

        Assert.Contains("BreakGlassInvoked", await AuditFor(reference));
    }

    [Fact]
    public async Task A_retrospective_needs_real_notes_and_clears_the_obligation_once_given()
    {
        var reference = await BlockedDraftAsync();
        await RequestOverrideAsync(reference, breakGlass: true);
        await _client.PostJsonAsync($"/api/changes/{reference}/override/approvals",
            new { role = "TracksideItLead", approver = "t.lead", decision = "Approved" });

        var thin = await _client.PostJsonAsync($"/api/changes/{reference}/override/retrospective",
            new { completedBy = "h.of.it", notes = "done" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, thin.StatusCode);

        var proper = await _client.PostJsonAsync($"/api/changes/{reference}/override/retrospective",
            new
            {
                completedBy = "h.of.it",
                notes = "Root cause was an expired certificate on the standby ingest node; renewal is now automated."
            });

        Assert.Equal(HttpStatusCode.OK, proper.StatusCode);
        var body = await proper.ReadJsonAsync();
        Assert.False(body.GetProperty("retrospectiveOutstanding").GetBoolean());

        Assert.Contains("RetrospectiveCompleted", await AuditFor(reference));
    }

    [Fact]
    public async Task An_override_can_be_revoked_before_it_is_spent()
    {
        var reference = await BlockedDraftAsync();
        await RequestOverrideAsync(reference);

        var revoke = await _client.PostJsonAsync($"/api/changes/{reference}/override/revoke",
            new { approver = "h.of.it" });

        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal("Revoked", (await revoke.ReadJsonAsync()).GetProperty("state").GetString());

        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostEmptyAsync($"/api/changes/{reference}/submit")).StatusCode);
    }

    // ------------------------------------------------------------------ the audit log

    [Fact]
    public async Task The_audit_log_verifies_after_everything_this_fixture_has_done_to_it()
    {
        var reference = await BlockedDraftAsync();
        await RequestOverrideAsync(reference);
        await _client.PostJsonAsync($"/api/changes/{reference}/override/approvals",
            new { role = "HeadOfIt", approver = "h.of.it", decision = "Approved" });

        var verify = await _client.GetAsync("/api/audit/verify");

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var body = await verify.ReadJsonAsync();
        Assert.True(body.GetProperty("isValid").GetBoolean());
        Assert.True(body.GetProperty("entryCount").GetInt32() > 0);

        // The answer says what it does not cover, so it is not over-read.
        Assert.Contains("high-water mark", body.GetProperty("scope").GetString()!);
    }

    [Fact]
    public async Task A_refusal_is_recorded_rather_than_discarded()
    {
        // An attempt that was stopped is exactly the thing a change process needs to be able to
        // count later: a rising number of them says the freeze policy is wrong somewhere.
        var reference = await BlockedDraftAsync();

        var entries = await _client.GetFromJsonAsync<JsonElement>($"/api/audit?subject={reference}", ApiClient.Json);
        var blocked = entries.EnumerateArray()
            .Single(e => e.GetProperty("action").GetString() == "ChangeBlockedByFreeze");

        Assert.Contains("Round 1", blocked.GetProperty("details").GetString()!);
    }

    [Fact]
    public async Task Every_audit_entry_links_to_the_one_before_it()
    {
        await BlockedDraftAsync();

        var entries = (await (await _client.GetAsync("/api/audit")).ReadJsonAsync())
            .EnumerateArray().ToArray();

        Assert.True(entries.Length >= 2);

        for (var i = 1; i < entries.Length; i++)
        {
            Assert.Equal(
                entries[i - 1].GetProperty("hash").GetString(),
                entries[i].GetProperty("previousHash").GetString());
        }
    }

    private async Task<string> AuditFor(string reference)
    {
        var response = await _client.GetAsync($"/api/audit?subject={reference}");
        return await response.Content.ReadAsStringAsync();
    }
}
