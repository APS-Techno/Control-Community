using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using APS.Controls.Vacuum.Rendering;
using Xunit;

namespace APS.Controls.Tests.Rendering;

public class PipeRendererSmokeTests
{
    private const float Thickness = 8f;
    private const float HighlightOffset = 2.4f;
    private const float HighlightThickness = 1.5f;

    private static Bitmap RenderInto(int w, int h, IReadOnlyList<PointF> pts,
        Color? overlay = null, PipeOverlayStyle style = default)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(255, 30, 30, 35));
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        PipeRenderer.DrawPipe(g, pts, Thickness, HighlightOffset, HighlightThickness, overlay, style);
        return bmp;
    }

    private static bool IsBackground(Color c) => c.R < 50 && c.G < 50 && c.B < 55;

    [Fact]
    public void DrawPipe_StraightHorizontal_DoesNotThrow_AndWritesPipePixelsOnCenterline()
    {
        var pts = new[] { new PointF(20, 50), new PointF(180, 50) };
        using var bmp = RenderInto(200, 100, pts);

        Assert.False(IsBackground(bmp.GetPixel(100, 50)));
    }

    [Fact]
    public void DrawPipe_NinetyDegreeBend_DrawsElbowAtCorner()
    {
        // Horizontal then up: right-then-up (turn negative because +Y is down).
        var pts = new[] { new PointF(20, 80), new PointF(150, 80), new PointF(150, 20) };
        using var bmp = RenderInto(200, 100, pts);

        // Centerline pixel a few px before the corner (still on the straight body) — pipe present.
        Assert.False(IsBackground(bmp.GetPixel(140, 80)));
        // Centerline pixel a few px after the corner (vertical leg) — pipe present.
        Assert.False(IsBackground(bmp.GetPixel(150, 70)));
        // Outside-corner pixel (upper-right of the bend vertex, well clear of any elbow geometry) — background.
        Assert.True(IsBackground(bmp.GetPixel(165, 65)));
    }

    [Fact]
    public void DrawPipe_NinetyDegreeBend_ElbowGradientReachesHighlightStops()
    {
        var pts = new[] { new PointF(20, 80), new PointF(150, 80), new PointF(150, 20) };
        using var bmp = RenderInto(200, 100, pts);

        // r = 4, vertex (150,80), uIn=(1,0), uOut=(0,-1): center (146,76), outer rim radius 8.
        // The gradient runs inner rim (BodyEdgeDark, bright) → outer rim (MetalDark, dark).
        // Compare two points on the 45° radial instead of exact RGB values, which drift with geometry.
        var innerRegion = bmp.GetPixel(147, 77);
        var outerRegion = bmp.GetPixel(150, 80);

        Assert.True(innerRegion.R > outerRegion.R,
            $"Elbow gradient should run brighter near the inner rim than the outer rim. " +
            $"Inner {innerRegion} should have higher R than outer {outerRegion}; gradient axis likely misaligned.");
    }

    [Fact]
    public void DrawPipe_AdaptiveClamp_HandlesShortMiddleSegment_NoOverlap()
    {
        // Z-shape with a 6 px middle segment — both elbows must coexist without overlap.
        var pts = new[]
        {
            new PointF(20, 50),
            new PointF(100, 50),
            new PointF(100, 56),
            new PointF(180, 56)
        };
        using var bmp = RenderInto(200, 100, pts);
        Assert.False(IsBackground(bmp.GetPixel(100, 53)));
    }

    [Fact]
    public void DrawPipe_WithCyanOverlay_CenterPixelIsBluerThanWithoutOverlay()
    {
        var pts = new[] { new PointF(20, 50), new PointF(180, 50) };
        using var withOverlay = RenderInto(200, 100, pts,
            Color.FromArgb(0, 200, 255),
            new PipeOverlayStyle(GlowThickness: 7f, GlowAlpha: 60, StripeThickness: 2.5f, StripeAlpha: 255));
        using var withoutOverlay = RenderInto(200, 100, pts);

        var with = withOverlay.GetPixel(100, 50);
        var without = withoutOverlay.GetPixel(100, 50);
        Assert.True(with.B > without.B, $"overlay should add blue: with.B={with.B}, without.B={without.B}");
    }

    [Fact]
    public void DrawPipe_BendWithOverlay_OverlayArcPresentInElbow()
    {
        var pts = new[] { new PointF(20, 80), new PointF(150, 80), new PointF(150, 20) };
        var style = new PipeOverlayStyle(7f, 60, 2.5f, 255);
        using var withOverlay = RenderInto(200, 100, pts, Color.FromArgb(0, 200, 255), style);
        using var withoutOverlay = RenderInto(200, 100, pts);

        // Mid-elbow centerline pixel: lies on the arc at ~45° between the two legs from the elbow center.
        // For r ≈ 8 with elbow center at (142, 72), 45° outward (down-right toward the convex side) lands near (147.7, 77.7).
        var with = withOverlay.GetPixel(148, 78);
        var without = withoutOverlay.GetPixel(148, 78);
        // Require a meaningful contribution (5 is well within the 255-alpha stripe's contribution at a centerline elbow pixel).
        Assert.True(with.B >= without.B + 5,
            $"overlay arc should add noticeable blue at the elbow midpoint; with.B={with.B}, without.B={without.B}");
    }
}
