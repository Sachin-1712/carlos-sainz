using FreezeManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FreezeManager.Infrastructure.CalendarSync.Seed;

/// <summary>
/// Compares the bundled seed against the last sync that came from a published source.
/// </summary>
/// <remarks>
/// A seed that no longer matches reality is worse than no seed: when upstream is down the engine
/// falls back to it and computes freeze windows for a season that does not exist. The failure is
/// silent, because a calendar with the wrong races in it still looks like a calendar. This is the
/// check that makes it loud.
/// </remarks>
public static class SeedDriftCheck
{
    public static async Task<SeedDriftReport> InspectAsync(
        FreezeDbContext db,
        string seedPath,
        int season,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (!File.Exists(seedPath))
        {
            return new SeedDriftReport(season, null, null, false, $"No seed file at '{seedPath}'.");
        }

        var provider = new SeedCalendarProvider(seedPath);
        var seed = await provider.FetchSeasonAsync(season, cancellationToken);

        var seedRounds = seed.Events.Count(e => e.Status != Domain.Calendar.EventStatus.Cancelled);
        var seedVerified = seed.Events.Count > 0 && seed.Events.All(e => e.IsVerified);

        var lastVerified = await db.CalendarSyncRuns
            .AsNoTracking()
            .Where(r => r.Season == season && r.Succeeded && r.FromPublishedSource)
            .OrderByDescending(r => r.StartedAtUtc)

            // Two syncs can share a timestamp; the identity breaks the tie so "latest" is
            // deterministic rather than whichever row the provider happened to return first.
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastVerified is null)
        {
            return new SeedDriftReport(
                season, seedRounds, null, seedVerified,
                "No sync from a published source has run yet, so there is nothing to compare the seed against.");
        }

        if (seedRounds == lastVerified.StoredRoundCount)
        {
            return new SeedDriftReport(
                season, seedRounds, lastVerified.StoredRoundCount, seedVerified, null);
        }

        return new SeedDriftReport(
            season, seedRounds, lastVerified.StoredRoundCount, seedVerified,
            $"The bundled seed has {seedRounds} active round(s) but the last verified sync stored "
            + $"{lastVerified.StoredRoundCount}. The seed is describing a different season, so a "
            + "cold start would compute freeze windows for races that are not happening and miss "
            + "ones that are. Regenerate it: GET /api/calendar/" + season + "/seed-export.");
    }
}

public sealed record SeedDriftReport(
    int Season,
    int? SeedRoundCount,
    int? LastVerifiedRoundCount,
    bool SeedIsVerified,
    string? Problem)
{
    public bool HasDrifted => Problem is not null && LastVerifiedRoundCount is not null;

    public bool IsClean => Problem is null;
}
