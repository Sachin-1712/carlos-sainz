using FreezeManager.Infrastructure.CalendarSync;
using FreezeManager.Infrastructure.Persistence;
using FreezeManager.Infrastructure.Tests.Support;
using Xunit;

namespace FreezeManager.Infrastructure.Tests.CalendarSync;

public class FallbackCalendarProviderTests
{
    private static readonly DateTime Fp1 = new(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Uses_the_primary_when_it_works()
    {
        var primary = StubCalendarProvider.Returning("live", CalendarSource.LiveUpstream, StubCalendarProvider.Weekend(1, Fp1));
        var fallback = StubCalendarProvider.Returning("seed", CalendarSource.Seed, StubCalendarProvider.Weekend(1, Fp1, CalendarSource.Seed));

        var result = await new FallbackCalendarProvider(primary, fallback).FetchSeasonAsync(2026);

        Assert.Equal(CalendarSource.LiveUpstream, result.Source);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task Falls_back_when_the_primary_throws_and_says_so_in_the_warnings()
    {
        var primary = StubCalendarProvider.Throwing("live", new HttpRequestException("503 Service Unavailable"));
        var fallback = StubCalendarProvider.Returning("seed", CalendarSource.Seed, StubCalendarProvider.Weekend(1, Fp1, CalendarSource.Seed));

        var result = await new FallbackCalendarProvider(primary, fallback).FetchSeasonAsync(2026);

        Assert.Equal(CalendarSource.Seed, result.Source);
        Assert.Single(result.Events);
        Assert.Contains(result.Warnings, w => w.Contains("live") && w.Contains("503"));
    }

    [Fact]
    public async Task A_cancellation_is_not_a_failure_to_fall_back_from()
    {
        var primary = StubCalendarProvider.Throwing("live", new OperationCanceledException());
        var fallback = StubCalendarProvider.Returning("seed", CalendarSource.Seed);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new FallbackCalendarProvider(primary, fallback).FetchSeasonAsync(2026));

        Assert.Equal(0, fallback.Calls);
    }
}
