using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreezeManager.Api.Tests.Support;

/// <summary>Thin helpers so the tests read as intent rather than as HTTP plumbing.</summary>
internal static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static Task<HttpResponseMessage> PostEmptyAsync(this HttpClient client, string url) =>
        client.PostAsync(url, content: null);

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, Json);

    /// <summary>Creates a complete draft that is ready to be submitted.</summary>
    public static object CompleteDraft(
        DateTimeOffset start,
        TimeSpan duration,
        string title = "Patch telemetry ingest",
        params string[] services) => new
        {
            title,
            requestedBy = "s.sindhe",
            description = "Apply the vendor security patch.",
            implementationPlan = "Rolling restart, one node at a time.",
            backoutPlan = "Restore the previous image.",
            type = "Normal",
            impact = "Medium",
            likelihood = "Low",
            affectedServiceKeys = services.Length == 0 ? new[] { "telemetry-ingest" } : services,
            requestedStartUtc = start,
            requestedEndUtc = start + duration
        };
}
