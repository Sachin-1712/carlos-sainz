using System.Text.Json.Serialization;
using FreezeManager.Api.Endpoints;
using FreezeManager.Infrastructure.Audit;
using FreezeManager.Infrastructure.CalendarSync;
using FreezeManager.Infrastructure.CalendarSync.Seed;
using FreezeManager.Infrastructure.Changes;
using FreezeManager.Infrastructure.Overrides;
using FreezeManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var season = builder.Configuration.GetValue("Freeze:Season", DateTime.UtcNow.Year);
var connectionString = builder.Configuration.GetConnectionString("Freeze") ?? "Data Source=freeze.db";

builder.Services.AddSingleton(new SeasonSettings(season));
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
    new SeedCalendarProvider(SeedPaths.Calendar(builder.Environment.ContentRootPath)),
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapChangeEndpoints();
app.MapApprovalEndpoints();
app.MapOverrideEndpoints();
app.MapFreezeEndpoints();
app.MapCalendarEndpoints();
app.MapAuditEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    service = "Race Weekend Change Freeze Manager",
    season,
    endpoints = new[]
    {
        "/api/changes", "/api/freeze/status", "/api/calendar/{season}", "/api/audit", "/api/audit/verify"
    }
}))
.ExcludeFromDescription();

// Apply migrations and make sure there is a service catalogue and a calendar to reason about.
// Skipped under test, where the fixture builds its own database and seeds what it needs.
if (!app.Environment.IsEnvironment("Testing"))
{
    await StartupTasks.PrepareAsync(app, season);
}

app.Run();

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

        var servicesPath = SeedPaths.Services(app.Environment.ContentRootPath);

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
