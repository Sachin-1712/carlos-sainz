using FreezeManager.Api.Contracts;
using FreezeManager.Infrastructure.Changes;

namespace FreezeManager.Api.Endpoints;

public static class FreezeEndpoints
{
    public static RouteGroupBuilder MapFreezeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/freeze").WithTags("Freeze");

        group.MapGet("/status", async (
            string serviceKey,
            DateTimeOffset? at,
            FreezeContextFactory factory,
            SeasonSettings season,
            TimeProvider time,
            CancellationToken cancellationToken) =>
        {
            var context = await factory.CreateAsync(season.Season, cancellationToken);

            if (!context.TiersByServiceKey.TryGetValue(serviceKey, out var tier))
            {
                return Results.NotFound(new { error = $"Unknown service '{serviceKey}'." });
            }

            var instant = at ?? time.GetUtcNow();
            var evaluation = context.Calculator.Evaluate(tier, instant);

            return Results.Ok(new FreezeStatusResponse
            {
                ServiceKey = serviceKey,
                Tier = tier.ToString(),
                Policy = evaluation.PolicyName,
                AtUtc = evaluation.EvaluatedAtUtc,
                IsFrozen = evaluation.IsFrozen,
                IsAdvisory = evaluation.IsAdvisory,
                Reason = evaluation.Reason,
                ActiveWindow = evaluation.ActiveWindow is null ? null : FreezeWindowResponse.From(evaluation.ActiveWindow),
                NextWindow = evaluation.NextWindow is null ? null : FreezeWindowResponse.From(evaluation.NextWindow),
                HoursUntilThaw = evaluation.TimeUntilThaw is { } thaw ? Math.Round(thaw.TotalHours, 2) : null,
                HoursUntilNextFreeze = evaluation.TimeUntilNextFreeze is { } next ? Math.Round(next.TotalHours, 2) : null
            });
        })
        .WithSummary("Whether a service is frozen at an instant, defaulting to now.");

        group.MapGet("/windows", async (
            string serviceKey,
            FreezeContextFactory factory,
            SeasonSettings season,
            CancellationToken cancellationToken) =>
        {
            var context = await factory.CreateAsync(season.Season, cancellationToken);

            if (!context.TiersByServiceKey.TryGetValue(serviceKey, out var tier))
            {
                return Results.NotFound(new { error = $"Unknown service '{serviceKey}'." });
            }

            var windows = context.Calculator.WindowsFor(tier);
            return Results.Ok(windows.Select(FreezeWindowResponse.From).ToArray());
        })
        .WithSummary("Every freeze window for a service across the season, merged.");

        group.MapGet("/next-window", async (
            string serviceKey,
            double? hours,
            DateTimeOffset? after,
            FreezeContextFactory factory,
            SeasonSettings season,
            TimeProvider time,
            CancellationToken cancellationToken) =>
        {
            var duration = TimeSpan.FromHours(hours is > 0 ? hours.Value : 4);
            var context = await factory.CreateAsync(season.Season, cancellationToken);

            if (!context.TiersByServiceKey.TryGetValue(serviceKey, out var tier))
            {
                return Results.NotFound(new { error = $"Unknown service '{serviceKey}'." });
            }

            var window = context.Calculator.NextOpenWindow(tier, after ?? time.GetUtcNow(), duration);

            return window is null
                ? Results.Ok(new { serviceKey, requestedHours = duration.TotalHours, suggestedWindow = (OpenWindowResponse?)null })
                : Results.Ok(new { serviceKey, requestedHours = duration.TotalHours, suggestedWindow = OpenWindowResponse.From(window) });
        })
        .WithSummary("The next window long enough to change a service.");

        group.MapGet("/services", async (
            FreezeContextFactory factory,
            SeasonSettings season,
            CancellationToken cancellationToken) =>
        {
            var context = await factory.CreateAsync(season.Season, cancellationToken);

            return Results.Ok(context.TiersByServiceKey
                .OrderBy(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => new { serviceKey = kvp.Key, tier = kvp.Value.ToString() })
                .ToArray());
        })
        .WithSummary("The service catalogue and each service's tier.");

        return group;
    }
}

/// <summary>Which season the API answers about.</summary>
public sealed record SeasonSettings(int Season);
