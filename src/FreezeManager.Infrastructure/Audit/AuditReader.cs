using FreezeManager.Domain.Audit;
using FreezeManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FreezeManager.Infrastructure.Audit;

public sealed class AuditReader
{
    private readonly FreezeDbContext _db;

    public AuditReader(FreezeDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<IReadOnlyList<AuditEntryRecord>> ReadAsync(
        string? subject = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.AuditEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(subject))
        {
            query = query.Where(e => e.Subject == subject);
        }

        query = query.OrderBy(e => e.Sequence);

        if (limit is > 0)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Verifies the whole log.
    /// </summary>
    /// <remarks>
    /// Two checks, not one. <see cref="AuditChain.Verify"/> recomputes each entry's hash from its
    /// contents and checks the links. Separately, the recomputed hash is compared against the hash
    /// as stored, which catches a row whose hash column was edited on its own.
    /// </remarks>
    public async Task<AuditChainVerification> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var records = await _db.AuditEntries
            .AsNoTracking()
            .OrderBy(e => e.Sequence)
            .ToListAsync(cancellationToken);

        foreach (var record in records)
        {
            var recomputed = record.ToDomain().Hash;

            if (!string.Equals(recomputed, record.StoredHash, StringComparison.Ordinal))
            {
                return AuditChainVerification.Broken(
                    records.Count,
                    record.Sequence,
                    $"Entry {record.Sequence} has been altered: the stored hash does not match its contents.");
            }
        }

        return AuditChain.Verify(records.Select(r => r.ToDomain()));
    }
}
