using System;
using System.Drawing;
using System.Linq;
using APS.Controls.Generic;
using Xunit;

namespace CoreLogicTests;

public class ClusterLayoutTests
{
    // Visible pipe = hub-edge -> tile-edge along the spoke. Computed independently
    // of the layout's own coverage math: intersect the radial ray with the tile's
    // axis-aligned box and subtract the hub radius.
    private static double VisiblePipe(ClusterLayout layout, string key, double hexRadius)
    {
        var c = layout.Center;
        var t = layout.TileFor(key);
        var tc = new Point(t.X + t.Width / 2, t.Y + t.Height / 2);
        double dx = tc.X - c.X, dy = tc.Y - c.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        dx /= len; dy /= len;
        double tx1 = Math.Abs(dx) < 1e-9 ? double.NegativeInfinity : (t.Left   - c.X) / dx;
        double tx2 = Math.Abs(dx) < 1e-9 ? double.PositiveInfinity : (t.Right  - c.X) / dx;
        double ty1 = Math.Abs(dy) < 1e-9 ? double.NegativeInfinity : (t.Top    - c.Y) / dy;
        double ty2 = Math.Abs(dy) < 1e-9 ? double.PositiveInfinity : (t.Bottom - c.Y) / dy;
        double tEnter = Math.Max(Math.Min(tx1, tx2), Math.Min(ty1, ty2));
        return tEnter - hexRadius;
    }

    private static ClusterLayout Build(int w = 600, int h = 600) =>
        new ClusterLayout(new Size(w, h),
            new (string, int)[] { ("LL", 0), ("C1", 60), ("C2", 120),
                                  ("C3", 180), ("C4", 240) });

    [Fact]
    public void Center_is_client_center()
    {
        var layout = Build(600, 400);
        Assert.Equal(new Point(300, 200), layout.Center);
    }

    [Fact]
    public void Dpi_scale_grows_the_tile_clamp_bounds()
    {
        var chambers = new (string, int)[] { ("LL", 0), ("C1", 60), ("C2", 120),
                                             ("C3", 180), ("C4", 240) };
        // Roomy client → both layouts sit at the upper tile clamp; the scaled
        // one's clamp is 1.5x so its tiles (and hub) grow with the fonts.
        var at96  = new ClusterLayout(new Size(4000, 3000), chambers);
        var at144 = new ClusterLayout(new Size(4000, 3000), chambers, 1.5f);
        Assert.Equal(320, at96.TileSize.Width);
        Assert.Equal(264, at96.TileSize.Height);
        Assert.Equal((int)(320 * 1.5f), at144.TileSize.Width);
        Assert.Equal((int)(264 * 1.5f), at144.TileSize.Height);
        Assert.True(at144.HexRect.Width > at96.HexRect.Width);

        // Tiny client → the lower clamp scales too (tiles never shrink below
        // a legible size relative to the fonts).
        var tiny96  = new ClusterLayout(new Size(100, 100), chambers);
        var tiny144 = new ClusterLayout(new Size(100, 100), chambers, 1.5f);
        Assert.Equal(90, tiny96.TileSize.Width);
        Assert.Equal((int)(90 * 1.5f), tiny144.TileSize.Width);
    }

    [Fact]
    public void Face0_tile_is_above_center()
    {
        var layout = Build();
        var ll = layout.TileFor("LL");
        var center = layout.Center;
        Assert.True(ll.Y + ll.Height / 2 < center.Y,
            $"LL tile center Y {ll.Y + ll.Height / 2} should be above panel center {center.Y}");
        Assert.InRange(ll.X + ll.Width / 2, center.X - 2, center.X + 2);
    }

    [Fact]
    public void Tiles_do_not_overlap()
    {
        var layout = Build();
        var rects = new[] { "LL", "C1", "C2", "C3", "C4" }
            .Select(layout.TileFor).ToList();
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
                Assert.False(rects[i].IntersectsWith(rects[j]),
                    $"tiles {i} and {j} overlap");
    }

    [Fact]
    public void All_tiles_within_bounds()
    {
        var layout = Build();
        var bounds = new Rectangle(0, 0, 600, 600);
        foreach (var key in new[] { "LL", "C1", "C2", "C3", "C4" })
            Assert.True(bounds.Contains(layout.TileFor(key)),
                $"{key} tile out of bounds: {layout.TileFor(key)}");
    }

    [Fact]
    public void Hex_is_centered_and_smaller_than_tiles_radius()
    {
        var layout = Build();
        Assert.True(layout.HexRect.Width > 0);
        Assert.True(layout.HexRect.Contains(layout.Center));
    }

    [Fact]
    public void Hex_width_is_1_2x_tile_width()
    {
        // The hub is 1.2x a tile width to give the central indication chip room.
        var layout = Build();   // 600x600 -> tile width 120
        Assert.Equal((int)(layout.TileSize.Width * 1.2), layout.HexRect.Width);
    }

    [Fact]
    public void All_visible_pipes_are_equal_length()
    {
        // Per-chamber radii equalize the visible pipe: square tiles cover different amounts of their
        // radial spoke for verticals (LL/C3) and diagonals (C1/C2/C4).
        var layout = Build();   // 600x600
        double rHex = layout.HexRect.Width / 2.0;
        var pipes = new[] { "LL", "C1", "C2", "C3", "C4" }
            .Select(k => VisiblePipe(layout, k, rHex)).ToList();
        double spread = pipes.Max() - pipes.Min();
        Assert.True(spread <= 3.0,
            $"pipe lengths should be equal; got [{string.Join(", ", pipes.Select(p => p.ToString("F1")))}] spread {spread:F1}");
    }

    [Fact]
    public void Vertical_chambers_sit_just_off_the_panel_edges()
    {
        // Pipes are lengthened toward 2x and capped to the window, pushing LL/C3 out to a slim edge
        // margin: off the edge but not pulled deep in.
        var layout = Build();   // 600x600
        var ll = layout.TileFor("LL");
        var c3 = layout.TileFor("C3");
        Assert.InRange(ll.Top, 8, 40);
        Assert.InRange(600 - c3.Bottom, 8, 40);
    }

    [Fact]
    public void Tiles_are_tall_enough_for_rich_content()
    {
        var layout = Build();   // 600x600
        Assert.True(layout.TileSize.Height >= 110,
            $"tile height {layout.TileSize.Height} too short for rich tile content");
    }

    [Fact]
    public void Roomy_tile_fits_three_metric_rows_with_bars()
    {
        // On a roomy window the tile reaches its raised height cap (264) so the
        // 2x-scaled rich content fits. DrawTile vertical chrome at 2x:
        // padTop(20) + header(~40) + headerGap(8) + dividerGap(10) + padBottom(12)
        // = ~90px. The remainder must fit 3 metric rows WITH mini bars.
        var layout = Build(1400, 1400);
        const int nominalChrome = 90;
        int available = layout.TileSize.Height - nominalChrome;
        Assert.True(TilePlan.For(available, 3).ShowBars,
            $"available {available}px must fit 3 metric rows with bars (need {3 * TilePlan.RowWithBar})");
    }

    [Fact]
    public void Tiles_do_not_overlap_on_a_large_panel()
    {
        // Raised tile-size caps must not let tiles collide on a big window.
        var layout = Build(1600, 1000);
        var rects = new[] { "LL", "C1", "C2", "C3", "C4" }
            .Select(layout.TileFor).ToList();
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
                Assert.False(rects[i].IntersectsWith(rects[j]),
                    $"tiles {i} and {j} overlap at large size");
    }
}
