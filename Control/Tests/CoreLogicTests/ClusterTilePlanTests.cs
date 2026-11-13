using APS.Controls.Generic;
using Xunit;

namespace CoreLogicTests;

public class ClusterTilePlanTests
{
    // Derive expectations from the row-height constants so these tests stay
    // valid regardless of the schematic scale factor.
    private const int Bar   = TilePlan.RowWithBar;
    private const int NoBar = TilePlan.RowNoBar;
    private const int Min   = TilePlan.RowMin;

    [Fact]
    public void No_metrics_means_empty_plan()
    {
        var plan = TilePlan.For(availableHeight: 10 * Bar, metricCount: 0);
        Assert.False(plan.ShowBars);
        Assert.Equal(0, plan.MetricRows);
    }

    [Fact]
    public void Tall_tile_shows_all_rows_with_bars()
    {
        var plan = TilePlan.For(3 * Bar, metricCount: 3);
        Assert.True(plan.ShowBars);
        Assert.Equal(3, plan.MetricRows);
        Assert.Equal(Bar, plan.RowHeight);
    }

    [Fact]
    public void Medium_tile_shows_all_rows_without_bars()
    {
        var plan = TilePlan.For(3 * NoBar, metricCount: 3);
        Assert.False(plan.ShowBars);
        Assert.Equal(3, plan.MetricRows);
        Assert.Equal(NoBar, plan.RowHeight);
    }

    [Fact]
    public void Tight_tile_compresses_rows_instead_of_dropping_them()
    {
        // One pixel below the no-bar threshold: all rows survive at a
        // compressed height between the floor and the normal height.
        var plan = TilePlan.For(3 * NoBar - 1, metricCount: 3);
        Assert.False(plan.ShowBars);
        Assert.Equal(3, plan.MetricRows);
        Assert.InRange(plan.RowHeight, Min, NoBar);

        // Densest legal packing: exactly the floor.
        var atFloor = TilePlan.For(4 * Min, metricCount: 4);
        Assert.Equal(4, atFloor.MetricRows);
        Assert.Equal(Min, atFloor.RowHeight, precision: 3);
    }

    [Fact]
    public void Below_the_floor_rows_drop_but_at_least_one_stays()
    {
        var plan = TilePlan.For(3 * Min - 1, metricCount: 3);
        Assert.False(plan.ShowBars);
        Assert.Equal(2, plan.MetricRows);

        var tiny = TilePlan.For(availableHeight: 1, metricCount: 3);
        Assert.Equal(1, tiny.MetricRows);
    }

    [Fact]
    public void Exact_bars_threshold_shows_bars()
    {
        var atThreshold = TilePlan.For(3 * Bar, metricCount: 3);
        Assert.True(atThreshold.ShowBars);
        Assert.Equal(3, atThreshold.MetricRows);

        // One pixel short — bars drop, all rows still fit (>= 3 * RowNoBar).
        var belowThreshold = TilePlan.For(3 * Bar - 1, metricCount: 3);
        Assert.False(belowThreshold.ShowBars);
        Assert.Equal(3, belowThreshold.MetricRows);
    }

    [Fact]
    public void Scale_multiplies_every_row_height_tier()
    {
        const float dpi = 1.5f;   // 144 DPI

        // Bars threshold scales: what fit bars at 96 DPI no longer does at 144.
        var scaled = TilePlan.For((int)(3 * Bar * dpi), metricCount: 3, scale: dpi);
        Assert.True(scaled.ShowBars);
        Assert.Equal(Bar * dpi, scaled.RowHeight, precision: 3);

        var tooShortForBars = TilePlan.For(3 * Bar, metricCount: 3, scale: dpi);
        Assert.False(tooShortForBars.ShowBars);

        // Compressed tier respects the scaled floor.
        var compressed = TilePlan.For((int)(3 * Min * dpi), metricCount: 3, scale: dpi);
        Assert.Equal(3, compressed.MetricRows);
        Assert.InRange(compressed.RowHeight, Min * dpi - 0.5f, NoBar * dpi);
    }

    [Fact]
    public void Zero_or_negative_height_keeps_one_row_when_metrics_exist()
    {
        Assert.Equal(1, TilePlan.For(availableHeight: 0, metricCount: 3).MetricRows);
        Assert.Equal(1, TilePlan.For(availableHeight: -5, metricCount: 3).MetricRows);
    }
}
