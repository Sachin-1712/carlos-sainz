using FreezeManager.Domain.Audit;
using FreezeManager.Domain.Overrides;
using FreezeManager.Infrastructure.Audit;
using FreezeManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FreezeManager.Infrastructure.Overrides;

/// <summary>
/// Expires grants that have run out, and flags break-glass retrospectives that have come due.
/// </summary>
/// <remarks>
/// An override that quietly stops working leaves no trace that permission was ever held. Expiry has
/// to be an event someone can find later, which means something has to notice it happening rather
/// than inferring it from a timestamp at read time. Each override is audited for expiry exactly
/// once, tracked by a flag on the row, so running the sweep more often does not multiply entries.
/// </remarks>
public sealed class OverrideExpirySweeper
{
    public const string SweeperActor = "system:override-sweeper";

    private readonly FreezeDbContext _db;
    private readonly AuditWriter _audit;
    private readonly TimeProvider _time;

    public OverrideExpirySweeper(FreezeDbContext db, AuditWriter audit, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(audit);

        _db = db;
        _audit = audit;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SweepResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var expired = 0;
        var overdue = 0;

        var granted = await _db.FreezeOverrides
            .Include(o => o.Approvals)
            .Where(o => o.State == OverrideState.Granted && !o.ExpiryAudited)
            .ToListAsync(cancellationToken);

        foreach (var record in granted)
        {
            var request = record.ToDomain();

            if (!request.ExpireIfElapsed(now))
            {
                continue;
            }

            record.ApplyFrom(request);
            record.ExpiryAudited = true;
            expired++;

            await _audit.AppendAsync(
                SweeperActor,
                AuditAction.OverrideExpired,
                record.ChangeReference,
                new
                {
                    incidentReference = record.IncidentReference,
                    grantedAtUtc = record.GrantedAtUtc,
                    expiredAtUtc = record.ExpiresAtUtc,
                    breakGlass = record.IsBreakGlass,
                    wasUsed = false
                },
                cancellationToken);
        }

        var awaitingRetrospective = await _db.FreezeOverrides
            .Include(o => o.Approvals)
            .Where(o => o.IsBreakGlass
                        && o.RetrospectiveDueAtUtc != null
                        && o.RetrospectiveCompletedAtUtc == null
                        && !o.RetrospectiveOverdueAudited)
            .ToListAsync(cancellationToken);

        foreach (var record in awaitingRetrospective)
        {
            if (record.ToDomain().RetrospectiveOverdueAt(now) is false)
            {
                continue;
            }

            record.RetrospectiveOverdueAudited = true;
            overdue++;

            await _audit.AppendAsync(
                SweeperActor,
                AuditAction.RetrospectiveOverdue,
                record.ChangeReference,
                new
                {
                    incidentReference = record.IncidentReference,
                    dueAtUtc = record.RetrospectiveDueAtUtc,
                    hoursOverdue = Math.Round((now - UtcTime.ToOffset(record.RetrospectiveDueAtUtc!.Value)).TotalHours, 2)
                },
                cancellationToken);
        }

        if (expired > 0 || overdue > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new SweepResult(expired, overdue);
    }
}

public sealed record SweepResult(int Expired, int RetrospectivesOverdue)
{
    public bool DidAnything => Expired > 0 || RetrospectivesOverdue > 0;
}
