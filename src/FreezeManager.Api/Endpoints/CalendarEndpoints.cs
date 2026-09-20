using FreezeManager.Infrastructure.CalendarSync;
using FreezeManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FreezeManager.Api.Endpoints;

public static class CalendarEndpoints
{
    public static RouteGroupBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/calendar").WithTags("Calendar");

        group.MapGet("/{season:int}", async (
            int season,
            CalendarRepository repository,
            FreezeDbContext db,
            CancellationToken cancellationToken) =>
        {
            var calendar = await repository.LoadSeasonAsync(season, cancellationToken);

            var provenance = await db.RaceEvents
                .AsNoTracking()
                .Where(e => e.Season == season)
                .ToDictionaryAsync(e => e.Round, e => new { e.Source, e.IsVerified, e.PinnedByAdmin }, cancellationToken);

            return Results.Ok(new
            {
                season = calendar.Season,
                events = calendar.Events.Select(e => new
                {
                    e.Round,
                    e.OfficialName,
                    e.Circuit,
                    e.Country,
                    e.LocalTimeZoneId,
                    format = e.Format.ToString(),
                    status = e.Status.ToString(),
                    firstSessionStartUtc = e.FirstSessionStartUtc,
                    parcFermeReleaseUtc = e.ParcFermeReleaseUtc,
                    sessions = e.Sessions.Select(s => new
                    {
                        type = s.Type.ToString(),
                        s.ScheduledStartUtc,
                        s.ScheduledEndUtc,
                        s.ActualStartUtc,
                        s.ActualEndUtc
                    }),
                    source = provenance.TryGetValue(e.Round, out var p) ? p.Source.ToString() : null,
                    isVerified = provenance.TryGetValue(e.Round, out var v) && v.IsVerified,
                    pinnedByAdmin = provenance.TryGetValue(e.Round, out var pin) && pin.PinnedByAdmin
                })
            });
        })
        .WithSummary("The stored calendar for a season, with each event's provenance.");

        group.MapPost("/{season:int}/sync", async (
            int season,
            CalendarSyncService sync,
            CancellationToken cancellationToken) =>
        {
            var result = await sync.SyncSeasonAsync(season, cancellationToken);

            return result.Succeeded
                ? Results.Ok(new
                {
                    result.Season,
                    provider = result.ProviderName,
                    source = result.Source?.ToString(),
                    result.Added,
                    result.Updated,
                    result.SkippedPinned,
                    result.Warnings,

                    // Each entry is a row to add to CircuitTimeZones. Named here so the fix does not
                    // require reading the warning prose.
                    unmappedCircuitIds = result.UnmappedCircuitIds
                })
                : Results.Problem(
                    title: "Calendar sync failed",
                    detail: result.Error,
                    statusCode: StatusCodes.Status502BadGateway);
        })
        .WithSummary("Pull a season from the calendar provider and reconcile it into the store.");

        group.MapGet("/sync-runs", async (FreezeDbContext db, CancellationToken cancellationToken) =>
        {
            var runs = await db.CalendarSyncRuns
                .AsNoTracking()
                .OrderByDescending(r => r.StartedAtUtc)
                .Take(25)
                .ToListAsync(cancellationToken);

            return Results.Ok(runs.Select(r => new
            {
                r.Season,
                r.Provider,
                source = r.Source?.ToString(),
                r.StartedAtUtc,
                r.CompletedAtUtc,
                r.Succeeded,
                r.EventsAdded,
                r.EventsUpdated,
                r.EventsSkipped,
                r.Message
            }));
        })
        .WithSummary("Recent calendar sync attempts, successful or not.");

        return group;
    }
}
