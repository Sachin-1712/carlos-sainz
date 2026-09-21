using System.Text.Json.Serialization;
using FreezeManager.Api.Endpoints;
using FreezeManager.Domain.Calendar;
using FreezeManager.Infrastructure.Audit;
using FreezeManager.Infrastructure.CalendarSync;
using FreezeManager.Infrastructure.CalendarSync.Seed;
using FreezeManager.Infrastructure.Changes;
using FreezeManager.Infrastructure.Overrides;
using FreezeManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using FreezeManager.Api.Components;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Compose static web assets in every environment, not only Development. Without this, a fresh
// clone running `dotnet run` (which defaults to Production) serves the page but 404s on
// blazor.web.js, so the dashboard renders and then never wires up. No-ops once published, where
// the assets are already copied into wwwroot.
builder.WebHost.UseStaticWebAssets();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Race Weekend Change Freeze Manager",
        Version = "v1",
        Description =
            "An IT change-management API in which the race calendar is a first-class scheduling "
            + "constraint. Submitting or scheduling a change runs the freeze gate; a refusal returns "
            + "409 with the conflicting windows, the next open window, and a ready-to-send retry. "
            + "Emergency overrides are attributable, time-boxed and approved, and everything is "
            + "recorded in a hash-chained audit log that GET /api/audit/verify can check."
    };

    return Task.CompletedTask;
}));
builder.Services.AddProblemDetails();

var season = builder.Configuration.GetValue("Freeze:Season", DateTime.UtcNow.Year);
var connectionString = builder.Configuration.GetConnectionString("Freeze") ?? "Data Source=freeze.db";

builder.Services.AddSingleton(new SeasonSettings(season));
builder.Services.AddSingleton(new SeedPathProvider(builder.Environment.ContentRootPath));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<FreezeDbContext>(options => options.UseFreezeDefaults(connectionString));

builder.Services.AddScoped<CalendarRepository>();
builder.Services.AddScoped<FreezeContextFactory>(sp =>
    new FreezeContextFactory(sp.GetRequiredService<CalendarRepository>()));

builder.Services.AddScoped<AuditWriter>(sp => new AuditWriter(
    sp.GetRequiredService<FreezeDbContext>(),
    sp.GetRequiredService<TimeProvider>()));

builder.Services.AddScoped<AuditReader>();

builder.Services.AddScoped<OverrideService>(sp => new OverrideService(
    sp.GetRequiredService<FreezeDbContext>(),
    sp.GetRequiredService<FreezeContextFactory>(),
    sp.GetRequiredService<AuditWriter>(),
    sp.GetRequiredService<SeasonSettings>().Season,
    sp.GetRequiredService<TimeProvider>()));

builder.Services.AddScoped<OverrideExpirySweeper>(sp => new OverrideExpirySweeper(
    sp.GetRequiredService<FreezeDbContext>(),
    sp.GetRequiredService<AuditWriter>(),
    sp.GetRequiredService<TimeProvider>()));

builder.Services.AddScoped<ChangeRequestService>(sp => new ChangeRequestService(
    sp.GetRequiredService<FreezeDbContext>(),
    sp.GetRequiredService<FreezeContextFactory>(),
    sp.GetRequiredService<AuditWriter>(),
    sp.GetRequiredService<OverrideService>(),
    sp.GetRequiredService<SeasonSettings>().Season,
    sp.GetRequiredService<TimeProvider>()));

// Live first, bundled seed as the fallback. A freeze tool that goes down because a third-party API
// is down is itself an outage, and one timed for a race weekend.
builder.Services.AddHttpClient<JolpicaCalendarProvider>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration.GetValue("Freeze:CalendarBaseUrl", JolpicaCalendarProvider.DefaultBaseUrl)!);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<IRaceCalendarProvider>(sp => new FallbackCalendarProvider(
    sp.GetRequiredService<JolpicaCalendarProvider>(),
    new SeedCalendarProvider(sp.GetRequiredService<SeedPathProvider>().CalendarPath),
    sp.GetRequiredService<ILogger<FallbackCalendarProvider>>()));

builder.Services.AddScoped<CalendarSyncService>(sp => new CalendarSyncService(
    sp.GetRequiredService<FreezeDbContext>(),
    sp.GetRequiredService<IRaceCalendarProvider>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<CalendarSyncService>>()));

// An override that quietly stops working leaves no trace that permission was ever held, so
// something has to notice expiry happening rather than inferring it at read time.
builder.Services.AddHostedService<OverrideExpiryBackgroundService>();

var app = builder.Build();

app.UseStatusCodePages();
app.UseAntiforgery();

// Browsable API documentation. Not gated behind Development: a reviewer cloning this repository
// should get something clickable on the first run, which was the whole point of adding it.
app.MapOpenApi();

app.MapScalarApiReference(options => options
    .WithTitle("Race Weekend Change Freeze Manager")
    .WithTheme(ScalarTheme.BluePlanet)
    .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl));

app.MapStaticAssets();

app.MapChangeEndpoints();
app.MapApprovalEndpoints();
app.MapOverrideEndpoints();
app.MapFreezeEndpoints();
app.MapCalendarEndpoints();
app.MapAuditEndpoints();

// The dashboard owns "/". A machine-readable index lives alongside it.
app.MapGet("/api", () => Results.Ok(new
{
    service = "Race Weekend Change Freeze Manager",
    season,
    documentation = "/scalar/v1",
    dashboard = "/",
    endpoints = new[]
    {
        "/api/changes", "/api/freeze/status", "/api/calendar/{season}",
        "/api/calendar/{season}/completeness", "/api/audit", "/api/audit/verify"
    }
}))
.ExcludeFromDescription();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Apply migrations and make sure there is a service catalogue and a calendar to reason about.
// Skipped under test, where the fixture builds its own database and seeds what it needs.
if (!app.Environment.IsEnvironment("Testing"))
{
    await StartupTasks.PrepareAsync(app, season);
}

app.Run();

/// <summary>Where the bundled seed files live, resolved once and shared.</summary>
public sealed class SeedPathProvider
{
    public SeedPathProvider(string contentRoot)
    {
        CalendarPath = SeedPaths.Calendar(contentRoot);
        ServicesPath = SeedPaths.Services(contentRoot);
    }

    public string CalendarPath { get; }

    public string ServicesPath { get; }
}

internal static class SeedPaths
{
    public static string Calendar(string contentRoot) => Resolve(contentRoot, "calendar-2026.json");

    public static string Services(string contentRoot) => Resolve(contentRoot, "services.json");

    private static string Resolve(string contentRoot, string fileName)
    {
        // Runs from the project directory in development and from the publish output in a container.
        var candidates = new[]
        {
            Path.Combine(contentRoot, "data", "seed", fileName),
            Path.Combine(contentRoot, "..", "..", "data", "seed", fileName),
            Path.Combine(AppContext.BaseDirectory, "data", "seed", fileName)
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}

internal static class StartupTasks
{
    public static async Task PrepareAsync(WebApplication app, int season)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        var db = scope.ServiceProvider.GetRequiredService<FreezeDbContext>();

        await db.Database.MigrateAsync();

        var servicesPath = scope.ServiceProvider.GetRequiredService<SeedPathProvider>().ServicesPath;

        if (File.Exists(servicesPath) && !await db.Services.AnyAsync())
        {
            var count = await ServiceCatalogSeeder.SeedFromFileAsync(db, servicesPath);
            logger.LogInformation("Seeded {Count} services.", count);
        }

        if (!await db.RaceEvents.AnyAsync(e => e.Season == season))
        {
            var sync = scope.ServiceProvider.GetRequiredService<CalendarSyncService>();
            var result = await sync.SyncSeasonAsync(season);

            logger.LogInformation(
                "Calendar bootstrap for {Season} from {Provider}: {Added} added, {Warnings} warnings.",
                season, result.ProviderName, result.Added, result.Warnings.Count);
        }

        await ReportCompletenessAsync(scope, logger, season);
        await ReportSeedDriftAsync(scope, logger, season);
    }

    /// <summary>
    /// Says loudly at startup when the bundled seed no longer matches reality.
    /// </summary>
    /// <remarks>
    /// A stale seed is not an inert file. When upstream is unreachable the engine falls back to it
    /// and computes freeze windows for a season that is not happening, which is the same failure as
    /// a missing round except it looks fine.
    /// </remarks>
    private static async Task ReportSeedDriftAsync(IServiceScope scope, ILogger logger, int season)
    {
        var db = scope.ServiceProvider.GetRequiredService<FreezeDbContext>();
        var seeds = scope.ServiceProvider.GetRequiredService<SeedPathProvider>();
        var report = await SeedDriftCheck.InspectAsync(db, seeds.CalendarPath, season);

        if (report.HasDrifted)
        {
            logger.LogWarning(
                "SEED OUT OF DATE for {Season}: {Problem}", season, report.Problem);
        }
        else if (report.Problem is not null)
        {
            logger.LogInformation("Seed check for {Season}: {Problem}", season, report.Problem);
        }
        else
        {
            logger.LogInformation(
                "Seed for {Season} matches the last verified sync ({Rounds} rounds).",
                season, report.SeedRoundCount);
        }
    }

    /// <summary>
    /// Says loudly at startup whether the calendar has holes in it.
    /// </summary>
    /// <remarks>
    /// A round that never loaded is a weekend the engine believes is open. Nothing inside the engine
    /// can fail safe for an event it has never seen, so the only defence is to say so where someone
    /// will read it.
    /// </remarks>
    private static async Task ReportCompletenessAsync(IServiceScope scope, ILogger logger, int season)
    {
        var repository = scope.ServiceProvider.GetRequiredService<CalendarRepository>();
        var report = CalendarCompleteness.Inspect(await repository.LoadSeasonAsync(season));

        if (report.HasUnprotectedWeekends)
        {
            logger.LogWarning(
                "CALENDAR INCOMPLETE for {Season}: rounds {Missing} are missing. Those weekends are "
                + "NOT protected by a freeze. See GET /api/calendar/{Season}/completeness.",
                season, string.Join(", ", report.MissingRounds), season);
        }
        else if (!report.IsComplete)
        {
            logger.LogWarning(
                "Calendar for {Season} has {Count} incomplete round(s). See GET /api/calendar/{Season}/completeness.",
                season, report.IncompleteRounds.Count, season);
        }
        else
        {
            logger.LogInformation("Calendar for {Season} is complete: {Rounds} rounds.", season, report.RoundsPresent);
        }
    }
}

/// <summary>
/// Runs the override expiry sweep on a timer.
/// </summary>
/// <remarks>
/// The sweep is also exposed as an endpoint so a test, or an operator, can run it at a chosen
/// instant rather than waiting for the timer.
/// </remarks>
internal sealed class OverrideExpiryBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OverrideExpiryBackgroundService> _logger;

    public OverrideExpiryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OverrideExpiryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<OverrideExpirySweeper>();
                var result = await sweeper.SweepAsync(stoppingToken);

                if (result.DidAnything)
                {
                    _logger.LogInformation(
                        "Override sweep: {Expired} expired, {Overdue} retrospectives overdue.",
                        result.Expired, result.RetrospectivesOverdue);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed sweep must not take the host down; the next tick tries again.
                _logger.LogError(exception, "Override expiry sweep failed.");
            }
        }
    }
}

/// <summary>Exposed so the test host can build this application.</summary>
public partial class Program;
