namespace FreezeManager.Infrastructure.Persistence;

/// <summary>
/// Conversion between the domain's <see cref="DateTimeOffset"/> and the store's UTC
/// <see cref="DateTime"/>.
/// </summary>
/// <remarks>
/// SQLite has no timestamp type. EF Core stores <see cref="DateTimeOffset"/> as text and cannot
/// translate ordering or comparison on it, so records hold UTC <see cref="DateTime"/> instead. The
/// domain already guarantees UTC, so the offset carries no information and nothing is lost.
/// </remarks>
internal static class UtcTime
{
    public static DateTime FromOffset(DateTimeOffset value) => value.UtcDateTime;

    public static DateTime? FromOffset(DateTimeOffset? value) => value?.UtcDateTime;

    public static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue ? ToOffset(value.Value) : null;
}
