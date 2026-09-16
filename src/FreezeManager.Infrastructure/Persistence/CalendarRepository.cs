using FreezeManager.Domain.Calendar;
using FreezeManager.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace FreezeManager.Infrastructure.Persistence;

/// <summary>Reads the store and hands back validated domain objects.</summary>
public sealed class CalendarRepository
{
    private readonly FreezeDbContext _db;

    public CalendarRepository(FreezeDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<RaceCalendar> LoadSeasonAsync(int season, CancellationToken cancellationToken = default)
    {
        var records = await _db.RaceEvents
            .AsNoTracking()
            .Include(e => e.Sessions)
            .Include(e => e.ParcFermeWindows)
            .Where(e => e.Season == season)
            .OrderBy(e => e.Round)
            .ToListAsync(cancellationToken);

        return new RaceCalendar(season, records.Select(r => r.ToDomain()));
    }

    public Task<bool> HasSeasonAsync(int season, CancellationToken cancellationToken = default) =>
        _db.RaceEvents.AnyAsync(e => e.Season == season, cancellationToken);

    public async Task<IReadOnlyList<ManagedService>> LoadServicesAsync(CancellationToken cancellationToken = default)
    {
        var records = await _db.Services
            .AsNoTracking()
            .OrderBy(s => s.Tier)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return records.Select(r => r.ToDomain()).ToArray();
    }
}
