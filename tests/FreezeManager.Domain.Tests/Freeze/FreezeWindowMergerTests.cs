using FreezeManager.Domain.Freeze;
using Xunit;

namespace FreezeManager.Domain.Tests.Freeze;

public class FreezeWindowMergerTests
{
    private static DateTimeOffset Utc(int day, int hour) =>
        new(2026, 6, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_empty_set_merges_to_nothing()
    {
        Assert.Empty(FreezeWindowMerger.Merge([]));
    }

    [Fact]
    public void Windows_with_a_real_gap_between_them_are_left_alone()
    {
        var merged = FreezeWindowMerger.Merge(
        [
            new FreezeWindow(Utc(1, 0), Utc(3, 0), "Round 8"),
            new FreezeWindow(Utc(10, 0), Utc(12, 0), "Round 9")
        ]);

        Assert.Equal(2, merged.Count);
        Assert.Equal(Utc(1, 0), merged[0].StartUtc);
        Assert.Equal(Utc(10, 0), merged[1].StartUtc);
    }

    [Fact]
    public void Overlapping_windows_collapse_into_one()
    {
        var merged = FreezeWindowMerger.Merge(
        [
            new FreezeWindow(Utc(1, 0), Utc(5, 0), "Round 8", rounds: [8]),
            new FreezeWindow(Utc(4, 0), Utc(9, 0), "Round 9", rounds: [9])
        ]);

        var only = Assert.Single(merged);
        Assert.Equal(Utc(1, 0), only.StartUtc);
        Assert.Equal(Utc(9, 0), only.EndUtc);
        Assert.Equal(new[] { 8, 9 }, only.Rounds);
    }

    [Fact]
    public void Touching_windows_merge_because_a_zero_length_gap_is_not_a_deployment_opportunity()
    {
        var merged = FreezeWindowMerger.Merge(
        [
            new FreezeWindow(Utc(1, 0), Utc(5, 0), "Round 8"),
            new FreezeWindow(Utc(5, 0), Utc(9, 0), "Round 9")
        ]);

        var only = Assert.Single(merged);
        Assert.Equal(Utc(1, 0), only.StartUtc);
        Assert.Equal(Utc(9, 0), only.EndUtc);
    }

    [Fact]
    public void A_window_fully_contained_in_another_does_not_shorten_it()
    {
        var merged = FreezeWindowMerger.Merge(
        [
            new FreezeWindow(Utc(1, 0), Utc(20, 0), "Tier 1 lead-in"),
            new FreezeWindow(Utc(5, 0), Utc(9, 0), "Session freeze")
        ]);

        var only = Assert.Single(merged);
        Assert.Equal(Utc(1, 0), only.StartUtc);
        Assert.Equal(Utc(20, 0), only.EndUtc);
    }

    [Fact]
    public void Input_order_does_not_matter()
    {
        var forwards = FreezeWindowMerger.Merge(
        [
            new FreezeWindow(Utc(1, 0), Utc(5, 0), "Round 8"),
            new FreezeWindow(Utc(4, 0), Utc(9, 0), "Round 9")
        ]);

        var backwards = FreezeWindowMerger.Merge(
        [
            new FreezeWindow(Utc(4, 0), Utc(9, 0), "Round 9"),
            new FreezeWindow(Utc(1, 0), Utc(5, 0), "Round 8")
        ]);

        Assert.Equal(forwards.Single().StartUtc, backwards.Single().StartUtc);
        Assert.Equal(forwards.Single().EndUtc, backwards.Single().EndUtc);
    }

    [Fact]
    public void A_triple_header_collapses_to_a_single_window_not_three()
    {
        // Three consecutive race weekends whose Tier 1 lead-in and tail overlap.
        var merged = FreezeWindowMerger.Merge(
        [
            new FreezeWindow(Utc(1, 0), Utc(8, 0), "Round 18 (Austin)", rounds: [18]),
            new FreezeWindow(Utc(7, 0), Utc(15, 0), "Round 19 (Mexico City)", rounds: [19]),
            new FreezeWindow(Utc(14, 0), Utc(22, 0), "Round 20 (Sao Paulo)", rounds: [20])
        ]);

        var only = Assert.Single(merged);
        Assert.Equal(Utc(1, 0), only.StartUtc);
        Assert.Equal(Utc(22, 0), only.EndUtc);
        Assert.Equal(new[] { 18, 19, 20 }, only.Rounds);
        Assert.Equal(TimeSpan.FromDays(21), only.Duration);
    }

    [Fact]
    public void Blocking_dominates_advisory_so_a_merge_never_downgrades_a_hard_freeze()
    {
        var merged = FreezeWindowMerger.Merge(
        [
            new FreezeWindow(Utc(1, 0), Utc(5, 0), "Corporate advisory", isAdvisory: true),
            new FreezeWindow(Utc(4, 0), Utc(9, 0), "Trackside hard freeze", isAdvisory: false)
        ]);

        Assert.False(Assert.Single(merged).IsAdvisory);
    }

    [Fact]
    public void Wholly_advisory_windows_stay_advisory()
    {
        var merged = FreezeWindowMerger.Merge(
        [
            new FreezeWindow(Utc(1, 0), Utc(5, 0), "FP1 advisory", isAdvisory: true),
            new FreezeWindow(Utc(4, 0), Utc(9, 0), "FP2 advisory", isAdvisory: true)
        ]);

        Assert.True(Assert.Single(merged).IsAdvisory);
    }

    [Fact]
    public void Merged_reasons_are_combined_and_deduplicated()
    {
        var merged = FreezeWindowMerger.Merge(
        [
            new FreezeWindow(Utc(1, 0), Utc(5, 0), "Round 8"),
            new FreezeWindow(Utc(4, 0), Utc(9, 0), "Round 9"),
            new FreezeWindow(Utc(6, 0), Utc(11, 0), "Round 9")
        ]);

        Assert.Equal("Round 8 + Round 9", Assert.Single(merged).Reason);
    }
}
