using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FreezeManager.Infrastructure.Persistence;

public sealed class FreezeDbContext : DbContext
{
    public FreezeDbContext(DbContextOptions<FreezeDbContext> options)
        : base(options)
    {
    }

    public DbSet<RaceEventRecord> RaceEvents => Set<RaceEventRecord>();

    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();

    public DbSet<ParcFermeWindowRecord> ParcFermeWindows => Set<ParcFermeWindowRecord>();

    public DbSet<ServiceRecord> Services => Set<ServiceRecord>();

    public DbSet<CalendarSyncRunRecord> CalendarSyncRuns => Set<CalendarSyncRunRecord>();

    /// <summary>
    /// SQLite stores timestamps as text and hands them back with <c>Kind = Unspecified</c>. Every
    /// timestamp in this store is UTC, so stamp the kind back on the way out.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RaceEventRecord>(entity =>
        {
            entity.ToTable("RaceEvents");
            entity.HasIndex(e => new { e.Season, e.Round }).IsUnique();
            entity.Property(e => e.OfficialName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Circuit).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Country).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LocalTimeZoneId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.UpstreamCircuitId).HasMaxLength(100);

            entity.HasMany(e => e.Sessions)
                .WithOne()
                .HasForeignKey(s => s.RaceEventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ParcFermeWindows)
                .WithOne()
                .HasForeignKey(w => w.RaceEventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SessionRecord>(entity =>
        {
            entity.ToTable("Sessions");
            entity.HasIndex(s => new { s.RaceEventId, s.Type }).IsUnique();
        });

        modelBuilder.Entity<ParcFermeWindowRecord>(entity =>
        {
            entity.ToTable("ParcFermeWindows");
            entity.Property(w => w.Label).HasMaxLength(200);
        });

        modelBuilder.Entity<ServiceRecord>(entity =>
        {
            entity.ToTable("Services");
            entity.HasIndex(s => s.Key).IsUnique();
            entity.Property(s => s.Key).HasMaxLength(100).IsRequired();
            entity.Property(s => s.Name).HasMaxLength(200).IsRequired();
            entity.Property(s => s.Owner).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<CalendarSyncRunRecord>(entity =>
        {
            entity.ToTable("CalendarSyncRuns");
            entity.Property(r => r.Provider).HasMaxLength(200).IsRequired();
        });
    }
}

internal sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }
}

internal sealed class UtcNullableDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public UtcNullableDateTimeConverter()
        : base(
            value => value.HasValue ? (value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime()) : value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value)
    {
    }
}
