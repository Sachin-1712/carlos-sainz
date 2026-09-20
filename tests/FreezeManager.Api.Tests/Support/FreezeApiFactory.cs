using FreezeManager.Domain.Calendar;
using FreezeManager.Domain.Services;
using FreezeManager.Infrastructure.CalendarSync;
using FreezeManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FreezeManager.Api.Tests.Support;

/// <summary>
/// The API over an in-memory SQLite database holding one known race weekend.
/// </summary>
/// <remarks>
/// FP1 is at <see cref="Fp1"/>. Under the default policies that puts the trackside freeze at
/// FP1-48h .. FP1+55h and race support at FP1-24h .. FP1+53h, so every expectation below can be
/// written as an offset from FP1. The calendar provider is replaced with a stub: these tests must
/// not depend on a third-party API being reachable.
/// </remarks>
public sealed class FreezeApiFactory : WebApplicationFactory<Program>
{
    public const int Season = 2026;

    /// <summary>FP1 of the only weekend in the test calendar: Friday 8 May 2026, 12:00 UTC.</summary>
    public static readonly DateTimeOffset Fp1 = new(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A fixed "now", a month before the weekend, so nothing depends on the wall clock.</summary>
    public static readonly DateTimeOffset Now = Fp1.AddDays(-30);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public FreezeApiFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FreezeDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<FreezeDbContext>(options => options.UseSqlite(_connection, FreezeDbOptions.Apply));

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));

            // No outbound HTTP from a test run.
            services.RemoveAll<IRaceCalendarProvider>();
            services.AddScoped<IRaceCalendarProvider>(_ => new StubSeasonProvider());

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FreezeDbContext>();
            db.Database.Migrate();
            Seed(db);
        });
    }

    public static void Seed(FreezeDbContext db)
    {
        if (db.Services.Any())
        {
            return;
        }

        db.Services.AddRange(
            new ServiceRecord { Key = "telemetry-ingest", Name = "Telemetry ingest", Tier = ServiceTier.Trackside, Owner = "Trackside IT Lead" },
            new ServiceRecord { Key = "mission-control", Name = "Mission control", Tier = ServiceTier.RaceSupport, Owner = "Head of Race Support" },
            new ServiceRecord { Key = "intranet", Name = "Intranet", Tier = ServiceTier.Corporate, Owner = "IT Service Desk Manager" });

        db.RaceEvents.Add(BuildWeekend(1, Fp1));
        db.SaveChanges();
    }

    public static RaceEventRecord BuildWeekend(int round, DateTimeOffset fp1)
    {
        var sessions = new List<SessionRecord>
        {
            new() { Type = SessionType.Practice1, ScheduledStartUtc = fp1.UtcDateTime, ScheduledEndUtc = fp1.AddHours(1).UtcDateTime },
            new() { Type = SessionType.Qualifying, ScheduledStartUtc = fp1.AddHours(25).UtcDateTime, ScheduledEndUtc = fp1.AddHours(26).UtcDateTime },
            new() { Type = SessionType.Race, ScheduledStartUtc = fp1.AddHours(48).UtcDateTime, ScheduledEndUtc = fp1.AddHours(50).UtcDateTime }
        };

        return new RaceEventRecord
        {
            Season = Season,
            Round = round,
            OfficialName = $"Round {round} Grand Prix",
            Circuit = "Testville Circuit",
            Country = "Testland",
            LocalTimeZoneId = "Europe/London",
            Format = EventFormat.Conventional,
            Status = EventStatus.Scheduled,
            Source = CalendarSource.Seed,
            IsVerified = false,
            SyncedAtUtc = Now.UtcDateTime,
            Sessions = sessions,
            ParcFermeWindows = ParcFermeDerivation.Derive(sessions, IngestionDefaults.Standard)
        };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Returns a second weekend, so the sync endpoint has something to do.</summary>
    private sealed class StubSeasonProvider : IRaceCalendarProvider
    {
        public string Name => "Stub provider";

        public Task<CalendarFetchResult> FetchSeasonAsync(int season, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CalendarFetchResult(
                Name,
                CalendarSource.LiveUpstream,
                new[] { BuildWeekend(1, Fp1), BuildWeekend(2, Fp1.AddDays(7)) },
                new[] { "stub provider: not a real upstream" }));
    }
}
