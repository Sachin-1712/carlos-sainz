using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreezeManager.Infrastructure.CalendarSync;

/// <summary>
/// Tries the primary provider and falls back on failure. A freeze tool that goes down because a
/// third-party API is down is itself an outage, and one timed for a race weekend.
/// </summary>
public sealed class FallbackCalendarProvider : IRaceCalendarProvider
{
    private readonly IRaceCalendarProvider _primary;
    private readonly IRaceCalendarProvider _fallback;
    private readonly ILogger<FallbackCalendarProvider> _logger;

    public FallbackCalendarProvider(
        IRaceCalendarProvider primary,
        IRaceCalendarProvider fallback,
        ILogger<FallbackCalendarProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);

        _primary = primary;
        _fallback = fallback;
        _logger = logger ?? NullLogger<FallbackCalendarProvider>.Instance;
    }

    public string Name => $"{_primary.Name}, falling back to {_fallback.Name}";

    public async Task<CalendarFetchResult> FetchSeasonAsync(int season, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _primary.FetchSeasonAsync(season, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Calendar provider {Primary} failed for season {Season}; using {Fallback}.",
                _primary.Name, season, _fallback.Name);

            var fallback = await _fallback.FetchSeasonAsync(season, cancellationToken);

            var warnings = new List<string>
            {
                $"{_primary.Name} failed ({exception.GetType().Name}: {exception.Message}); used {_fallback.Name} instead."
            };
            warnings.AddRange(fallback.Warnings);

            return new CalendarFetchResult(fallback.ProviderName, fallback.Source, fallback.Events, warnings);
        }
    }
}
