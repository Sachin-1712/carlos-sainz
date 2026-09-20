using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FreezeManager.Infrastructure.Persistence;

/// <summary>Provider options every host of <see cref="FreezeDbContext"/> should apply.</summary>
public static class FreezeDbOptions
{
    /// <summary>
    /// Splits queries that load more than one collection.
    /// </summary>
    /// <remarks>
    /// A race event has both sessions and parc ferme windows. Loading both in one query multiplies
    /// the rows together, which is what EF Core's MultipleCollectionIncludeWarning is warning about.
    /// Setting it here rather than calling <c>AsSplitQuery</c> at each call site means a new query
    /// cannot reintroduce the warning by forgetting. Every affected query has a deterministic
    /// <c>OrderBy</c>, which is the condition split queries need to stay consistent.
    /// </remarks>
    public static void Apply(SqliteDbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
    }

    /// <summary>
    /// Everything a correctly configured context needs: split queries, and the interceptor that
    /// makes the audit log append-only. Hosts call this rather than assembling it themselves, so a
    /// context configured without the guarantee cannot be created by accident.
    /// </summary>
    public static DbContextOptionsBuilder UseFreezeDefaults(
        this DbContextOptionsBuilder builder,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseSqlite(connectionString, Apply).AddInterceptors(new AppendOnlyAuditInterceptor());
    }

    public static DbContextOptionsBuilder UseFreezeDefaults(
        this DbContextOptionsBuilder builder,
        System.Data.Common.DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseSqlite(connection, Apply).AddInterceptors(new AppendOnlyAuditInterceptor());
    }

    // Generic overloads so a typed builder stays typed; without them the caller loses
    // DbContextOptions<TContext> and cannot construct the context.
    public static DbContextOptionsBuilder<TContext> UseFreezeDefaults<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        string connectionString)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        UseFreezeDefaults((DbContextOptionsBuilder)builder, connectionString);
        return builder;
    }

    public static DbContextOptionsBuilder<TContext> UseFreezeDefaults<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        System.Data.Common.DbConnection connection)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        UseFreezeDefaults((DbContextOptionsBuilder)builder, connection);
        return builder;
    }
}
