using FreezeManager.Domain.Services;
using FreezeManager.Infrastructure.Persistence;
using FreezeManager.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FreezeManager.Infrastructure.Tests.Persistence;

public class ServiceCatalogSeederTests
{
    [Fact]
    public async Task Seeds_the_bundled_catalogue_across_all_three_tiers()
    {
        using var database = new TestDatabase();

        await using var db = database.CreateContext();
        var written = await ServiceCatalogSeeder.SeedFromFileAsync(db, Fixtures.SeedServicesPath);

        Assert.Equal(12, written);

        var services = await new CalendarRepository(db).LoadServicesAsync();
        Assert.Equal(12, services.Count);
        Assert.Contains(services, s => s.Tier == ServiceTier.Trackside);
        Assert.Contains(services, s => s.Tier == ServiceTier.RaceSupport);
        Assert.Contains(services, s => s.Tier == ServiceTier.Corporate);
    }

    [Fact]
    public async Task Seeding_twice_updates_in_place_rather_than_duplicating()
    {
        using var database = new TestDatabase();

        await using (var db = database.CreateContext())
        {
            await ServiceCatalogSeeder.SeedFromFileAsync(db, Fixtures.SeedServicesPath);
        }

        await using (var db = database.CreateContext())
        {
            await ServiceCatalogSeeder.SeedFromFileAsync(db, Fixtures.SeedServicesPath);
            Assert.Equal(12, await db.Services.CountAsync());
        }
    }

    [Fact]
    public async Task Service_records_round_trip_to_the_domain()
    {
        using var database = new TestDatabase();

        await using var db = database.CreateContext();
        await ServiceCatalogSeeder.SeedFromFileAsync(db, Fixtures.SeedServicesPath);

        var telemetry = (await new CalendarRepository(db).LoadServicesAsync()).Single(s => s.Key == "telemetry-ingest");

        Assert.Equal(ServiceTier.Trackside, telemetry.Tier);
        Assert.Equal("Trackside IT Lead", telemetry.Owner);
    }
}
