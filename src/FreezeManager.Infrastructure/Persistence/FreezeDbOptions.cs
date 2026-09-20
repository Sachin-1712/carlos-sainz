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
}
