using System.Text.Json;
using FreezeManager.Infrastructure.CalendarSync.Ergast;

namespace FreezeManager.Infrastructure.CalendarSync;

/// <summary>
/// Live calendar from the Jolpica API, which serves the Ergast response shape.
/// </summary>
public sealed class JolpicaCalendarProvider : IRaceCalendarProvider
{
    public const string DefaultBaseUrl = "https://api.jolpi.ca/ergast/f1/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly IngestionDefaults _defaults;
    private readonly TimeProvider _time;

    public JolpicaCalendarProvider(HttpClient http, IngestionDefaults? defaults = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
        _defaults = defaults ?? IngestionDefaults.Standard;
        _time = time ?? TimeProvider.System;
    }

    public string Name => "Jolpica (Ergast-compatible)";

    public async Task<CalendarFetchResult> FetchSeasonAsync(int season, CancellationToken cancellationToken = default)
    {
        var baseUri = _http.BaseAddress ?? new Uri(DefaultBaseUrl);
        var requestUri = new Uri(baseUri, $"{season}/races/?format=json&limit=100");

        using var response = await _http.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var payload = await JsonSerializer.DeserializeAsync<ErgastResponse>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The calendar API returned an empty body.");

        return ErgastCalendarMapper.Map(season, payload, _defaults, _time.GetUtcNow(), Name);
    }
}
