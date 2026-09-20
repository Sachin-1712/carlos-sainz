using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FreezeManager.Infrastructure.Persistence;

/// <summary>
/// Refuses any attempt to update or delete an audit row.
/// </summary>
/// <remarks>
/// The hash chain makes tampering <i>detectable</i>; this makes the ordinary way of doing it
/// <i>impossible</i>. Without it, a stray <c>SaveChanges</c> on a tracked audit entity would rewrite
/// history silently and only the next verification would notice. It is enforced in the data layer
/// rather than in each service, because the guarantee has to hold for code that has not been
/// written yet.
/// </remarks>
public sealed class AppendOnlyAuditInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Guard(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<AuditEntryRecord>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new AuditLogIsAppendOnlyException(
                    $"Audit entry {entry.Entity.Sequence} cannot be {entry.State.ToString().ToLowerInvariant()}: the audit log is append-only.");
            }
        }
    }
}

public sealed class AuditLogIsAppendOnlyException : InvalidOperationException
{
    public AuditLogIsAppendOnlyException(string message)
        : base(message)
    {
    }
}
