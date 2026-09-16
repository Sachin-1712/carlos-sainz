using FreezeManager.Domain.Calendar;
using FreezeManager.Infrastructure.CalendarSync.Seed;
using FreezeManager.Infrastructure.Persistence;
using FreezeManager.Infrastructure.Tests.Support;
using Xunit;

namespace FreezeManager.Infrastructure.Tests.CalendarSync;

/// <summary>These run against the real bundled seed file, so they are also a lint of that file.</summary>
public class SeedCalendarProviderTests
{
    private static async Task<IReadOnlyList<RaceEventRecord>> LoadSeed()
    {
        var provider = new SeedCalendarProvider(Fixtures.SeedCalendarPath);
        var result = await provider.FetchSeasonAsync(2026);
        return result.Events;
    }

    [Fact]
    public async Task The_bundled_seed_is_a_full_season()
    {
        var events = await LoadSeed();

        Assert.Equal(24, events.Count);
        Assert.Equal(Enumerable.Range(1, 24), events.Select(e => e.Round));
    }

    [Fact]
    public async Task Every_seeded_event_is_marked_unverified_because_nobody_has_checked_it()
    {
        var events = await LoadSeed();

        Assert.All(events, e =>
        {
            Assert.False(e.IsVerified);
            Assert.Equal(CalendarSource.Seed, e.Source);
        });
    }

    [Fact]
    public async Task The_seed_provider_warns_once_per_unverified_round()
    {
        var provider = new SeedCalendarProvider(Fixtures.SeedCalendarPath);
        var result = await provider.FetchSeasonAsync(2026);

        Assert.Equal(24, result.Warnings.Count(w => w.Contains("unverified")));
    }

    [Fact]
    public async Task Every_seeded_event_has_a_race_and_derived_parc_ferme()
    {
        var events = await LoadSeed();

        Assert.All(events, e =>
        {
            Assert.Contains(e.Sessions, s => s.Type == SessionType.Race);
            Assert.NotEmpty(e.ParcFermeWindows);
            Assert.All(e.ParcFermeWindows, w => Assert.True(w.IsDerived));
        });
    }

    [Fact]
    public async Task Seeded_rounds_run_in_chronological_order()
    {
        var events = await LoadSeed();

        for (var i = 1; i < events.Count; i++)
        {
            var previousEnd = events[i - 1].Sessions.Max(s => s.ScheduledEndUtc);
            var nextStart = events[i].Sessions.Min(s => s.ScheduledStartUtc);

            Assert.True(nextStart > previousEnd, $"Round {events[i].Round} starts before round {events[i - 1].Round} ends.");
        }
    }

    [Fact]
    public async Task Sprint_weekends_in_the_seed_carry_sprint_sessions_and_two_parc_ferme_windows()
    {
        var events = await LoadSeed();
        var sprints = events.Where(e => e.Format == EventFormat.Sprint).ToArray();

        Assert.NotEmpty(sprints);
        Assert.All(sprints, e =>
        {
            Assert.Contains(e.Sessions, s => s.Type == SessionType.Sprint);
            Assert.Equal(2, e.ParcFermeWindows.Count);
        });
    }

    [Fact]
    public async Task Every_seeded_event_converts_to_a_valid_domain_event()
    {
        var events = await LoadSeed();

        foreach (var record in events)
        {
            var domain = record.ToDomain();
            Assert.Equal(record.Round, domain.Round);
            Assert.NotNull(domain.ParcFermeReleaseUtc);
        }
    }

    [Fact]
    public async Task Asking_for_a_season_the_seed_does_not_cover_loads_nothing_and_says_why()
    {
        var provider = new SeedCalendarProvider(Fixtures.SeedCalendarPath);
        var result = await provider.FetchSeasonAsync(2027);

        Assert.Empty(result.Events);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("2027", warning);
    }
}
