using System.Text.Json;
using FreezeManager.Domain.Audit;
using FreezeManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FreezeManager.Infrastructure.Audit;

/// <summary>Appends to the audit log, linking each entry to the one before it.</summary>
public sealed class AuditWriter
{
    private static readonly JsonSerializerOptions DetailOptions = new(JsonSerializerDefaults.Web);

    private readonly FreezeDbContext _db;
    private readonly TimeProvider _time;

    public AuditWriter(FreezeDbContext db, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Stages one entry. The caller saves, so the audit entry commits in the same transaction as
    /// the thing it describes -- an action that happened without being recorded, or a record of
    /// something that did not happen, are both worse than failing the whole operation.
    /// </summary>
    public async Task<AuditEntry> AppendAsync(
        string actor,
        AuditAction action,
        string subject,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        var tail = await _db.AuditEntries
            .OrderByDescending(e => e.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

        // Entries staged in this unit of work are not in the table yet, so check the tracker too.
        var staged = _db.ChangeTracker.Entries<AuditEntryRecord>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .OrderByDescending(e => e.Sequence)
            .FirstOrDefault();

        var previous = (tail, staged) switch
        {
            (null, null) => null,
            (not null, null) => tail,
            (null, not null) => staged,
            _ => staged!.Sequence > tail!.Sequence ? staged : tail
        };

        var sequence = (previous?.Sequence ?? 0) + 1;
        var previousHash = previous?.Hash ?? AuditEntry.GenesisHash;

        var entry = new AuditEntry(
            sequence,
            _time.GetUtcNow(),
            actor,
            action,
            subject,
            details is null ? "{}" : JsonSerializer.Serialize(details, DetailOptions),
            previousHash);

        _db.AuditEntries.Add(new AuditEntryRecord
        {
            Sequence = entry.Sequence,
            OccurredAtUtc = entry.OccurredAtUtc.UtcDateTime,
            Actor = entry.Actor,
            Action = entry.Action,
            Subject = entry.Subject,
            Details = entry.Details,
            PreviousHash = entry.PreviousHash,
            Hash = entry.Hash
        });

        return entry;
    }
}
