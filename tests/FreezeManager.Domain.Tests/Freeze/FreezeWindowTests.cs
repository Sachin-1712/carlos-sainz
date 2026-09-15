using FreezeManager.Domain.Freeze;
using Xunit;

namespace FreezeManager.Domain.Tests.Freeze;

public class FreezeWindowTests
{
    private static DateTimeOffset Utc(int day, int hour) =>
        new(2026, 6, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_window_must_end_after_it_starts()
    {
        Assert.Throws<ArgumentException>(() =>
            new FreezeWindow(Utc(10, 12), Utc(10, 12), "zero length"));

        Assert.Throws<ArgumentException>(() =>
            new FreezeWindow(Utc(10, 12), Utc(10, 11), "backwards"));
    }

    [Fact]
    public void A_window_requires_a_reason_because_the_reason_is_shown_to_the_blocked_engineer()
    {
        Assert.Throws<ArgumentException>(() => new FreezeWindow(Utc(10, 9), Utc(10, 17), "   "));
    }

    [Fact]
    public void Boundaries_are_half_open_so_adjacent_windows_never_both_claim_an_instant()
    {
        var window = new FreezeWindow(Utc(10, 9), Utc(10, 17), "FP1 to parc ferme release");

        Assert.True(window.Contains(Utc(10, 9)));      // the first frozen instant
        Assert.True(window.Contains(Utc(10, 16)));
        Assert.False(window.Contains(Utc(10, 17)));    // the first instant you may deploy again
        Assert.False(window.Contains(Utc(10, 8)));
    }

    [Fact]
    public void Inputs_in_any_offset_are_normalised_to_utc()
    {
        // 14:00 in Melbourne (UTC+11) is 03:00 UTC. A window built from circuit-local time must
        // compare correctly against a factory-local query.
        var melbourneAfternoon = new DateTimeOffset(2026, 3, 6, 14, 0, 0, TimeSpan.FromHours(11));
        var window = new FreezeWindow(melbourneAfternoon, melbourneAfternoon.AddHours(2), "FP1");

        Assert.Equal(TimeSpan.Zero, window.StartUtc.Offset);
        Assert.Equal(new DateTimeOffset(2026, 3, 6, 3, 0, 0, TimeSpan.Zero), window.StartUtc);
        Assert.True(window.Contains(new DateTimeOffset(2026, 3, 6, 3, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void A_change_that_spans_a_freeze_overlaps_it_and_is_not_truncated()
    {
        var freeze = new FreezeWindow(Utc(12, 8), Utc(15, 22), "Round 9 race weekend");

        // Starts before, ends after: the whole freeze sits inside the proposed change window.
        Assert.True(freeze.Overlaps(Utc(11, 0), Utc(16, 0)));

        // Clips the leading edge only.
        Assert.True(freeze.Overlaps(Utc(11, 0), Utc(12, 9)));

        // Clips the trailing edge only.
        Assert.True(freeze.Overlaps(Utc(15, 21), Utc(16, 0)));
    }

    [Fact]
    public void A_change_that_ends_exactly_when_the_freeze_starts_is_allowed()
    {
        var freeze = new FreezeWindow(Utc(12, 8), Utc(15, 22), "Round 9 race weekend");

        Assert.False(freeze.Overlaps(Utc(11, 0), Utc(12, 8)));
        Assert.False(freeze.Overlaps(Utc(15, 22), Utc(16, 0)));
    }

    [Fact]
    public void Duration_is_reported_for_scheduling_arithmetic()
    {
        var window = new FreezeWindow(Utc(12, 8), Utc(15, 20), "Round 9 race weekend");

        Assert.Equal(TimeSpan.FromHours(84), window.Duration);
    }

    [Fact]
    public void Contributing_rounds_are_deduplicated_and_ordered()
    {
        var window = new FreezeWindow(Utc(12, 8), Utc(15, 20), "Triple header", rounds: [20, 18, 19, 18]);

        Assert.Equal(new[] { 18, 19, 20 }, window.Rounds);
    }
}
