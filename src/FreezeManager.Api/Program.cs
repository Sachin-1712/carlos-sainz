using System.Text.Json.Serialization;
using FreezeManager.Api.Endpoints;
using FreezeManager.Infrastructure.CalendarSync;
using FreezeManager.Infrastructure.CalendarSync.Seed;
using FreezeManager.Infrastructure.Changes;
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
builder.Services.AddDbContext<FreezeDbContext>(options =>
    options.UseSqlite(connectionString, FreezeDbOptions.Apply));

builder.Services.AddScoped<CalendarRepository>();
builder.Services.AddScoped<FreezeContextFactory>(sp =>
    new FreezeContextFactory(sp.GetRequiredService<CalendarRepository>()));

builder.Services.AddScoped<ChangeRequestService>(sp => new ChangeRequestService(
    sp.GetRequiredService<FreezeDbContext>(),
    sp.GetRequiredService<FreezeContextFactory>(),
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

var app = builder.Build();

app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapChangeEndpoints();
app.MapFreezeEndpoints();
app.MapCalendarEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    service = "Race Weekend Change Freeze Manager",
    season,
    endpoints = new[] { "/api/changes", "/api/freeze/status", "/api/calendar/{season}" }
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

/// <summary>Exposed so the test host can build this application.</summary>
public partial class Program;
