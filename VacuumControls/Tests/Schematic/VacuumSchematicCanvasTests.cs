using System.Collections.Generic;
using System.Drawing;
using APS.Controls.Theme;
using APS.Controls.Vacuum.Schematic;
using APS.Controls.XML.GUI;
using Xunit;

namespace APS.Controls.Tests.Schematic;

public class VacuumSchematicCanvasTests
{
    private static VacuumSchematicCanvas NewCanvas(int width, int height)
    {
        var c = new VacuumSchematicCanvas();
        c.SetBounds(0, 0, width, height);
        return c;
    }

    [Fact]
    public void CanvasArea_WiderThanAspect_LetterboxesHorizontally()
    {
        using var c = NewCanvas(2000, 900);  // 20:9 — wider than 16:9
        Assert.Equal(new Rectangle(200, 0, 1600, 900), c.CanvasArea);
    }

    [Fact]
    public void CanvasArea_TallerThanAspect_LetterboxesVertically()
    {
        using var c = NewCanvas(1600, 1080);
        Assert.Equal(new Rectangle(0, 90, 1600, 900), c.CanvasArea);
    }

    [Fact]
    public void CanvasScale_AtExactReferenceWidth_IsOne()
    {
        // Taller-than-aspect → width-limited at exactly the 2200 px reference.
        using var c = NewCanvas(2200, 2000);
        Assert.Equal(2200, c.CanvasArea.Width);
        Assert.Equal(1.0f, c.CanvasScale);
    }

    [Fact]
    public void CanvasScale_AboveReferenceWidth_GrowsAboveOne()
    {
        // 4K-class canvas (4000 px wide letterbox) → widgets/pipes grow ~1.82×.
        using var c = NewCanvas(4000, 2250);
        Assert.Equal(4000, c.CanvasArea.Width);
        Assert.InRange(c.CanvasScale, 1.80f, 1.84f);
    }

    [Fact]
    public void CanvasScale_VeryWideCanvas_ClampsAtMax()
    {
        // 6000 px wide would compute 2.73× — clamped to the 2.5 ceiling.
        using var c = NewCanvas(6000, 4000);
        Assert.Equal(6000, c.CanvasArea.Width);
        Assert.Equal(2.5f, c.CanvasScale);
    }

    [Fact]
    public void CanvasScale_BelowReferenceWidth_ScalesDown()
    {
        using var c = NewCanvas(1100, 619);
        Assert.InRange(c.CanvasScale, 0.49f, 0.51f);
    }

    [Fact]
    public void PercentToPixel_AtFiftyPercent_MapsToCanvasCenter()
    {
        using var c = NewCanvas(1600, 900);
        var pt = c.PercentToPixel(50f, 50f);
        Assert.Equal(new Point(800, 450), pt);
    }

    [Fact]
    public void PixelToPercent_RoundTrip()
    {
        using var c = NewCanvas(1600, 900);
        var pt = c.PercentToPixel(30f, 70f);
        var (px, py) = c.PixelToPercent(pt.X, pt.Y);
        Assert.InRange(px, 29.9f, 30.1f);
        Assert.InRange(py, 69.9f, 70.1f);
    }

    [Fact]
    public void AddComponent_PositionsChildAtPercent_AndResizeReposions()
    {
        using var c = NewCanvas(1600, 900);
        using var child = new System.Windows.Forms.Panel { Size = new Size(40, 40) };
        c.AddComponent(child, 50f, 50f);
        Assert.Equal(new Point(780, 430), child.Location);

        c.SetBounds(0, 0, 3200, 1800);
        Assert.Equal(new Point(1580, 880), child.Location);
    }

    private sealed class StubAnchorControl : ThemedVacuumControl
    {
        public StubAnchorControl(string id, Point anchor)
        {
            ID = id;
            Size = new Size(40, 40);
            _anchor = anchor;
        }
        private readonly Point _anchor;
        public override Point GetAnchorPoint(string name) => Location + (Size)_anchor;
        public override IEnumerable<string> GetAnchorNames() => new[] { "p" };
    }

    [Fact]
    public void ResolveEndpoint_FreePoint_ReturnsCanvasPercentLocation()
    {
        using var c = NewCanvas(1600, 900);
        var pt = c.ResolveEndpoint("@25,50");
        Assert.NotNull(pt);
        Assert.Equal(new PointF(400, 450), pt!.Value);
    }

    [Fact]
    public void ResolveEndpoint_IdAnchor_ResolvesViaChildControl()
    {
        using var c = NewCanvas(1600, 900);
        var child = new StubAnchorControl("V1", new Point(10, 10));
        c.AddComponent(child, 50f, 50f);
        var pt = c.ResolveEndpoint("V1:p");
        Assert.NotNull(pt);
        Assert.Equal(child.GetAnchorPoint("p"), Point.Round(pt!.Value));
    }

    [Fact]
    public void ResolveEndpoint_Malformed_ReturnsNull()
    {
        using var c = NewCanvas(1600, 900);
        Assert.Null(c.ResolveEndpoint(null));
        Assert.Null(c.ResolveEndpoint(""));
        Assert.Null(c.ResolveEndpoint("not-a-spec"));
        Assert.Null(c.ResolveEndpoint("@"));
        Assert.Null(c.ResolveEndpoint("@1"));
        Assert.Null(c.ResolveEndpoint("@a,b"));
    }

    [Fact]
    public void SetZoom_CursorAnchorPreservesPercentUnderCursor()
    {
        using var c = NewCanvas(1600, 900);
        const int mx = 1000, my = 300;
        var (bx, by) = c.PixelToPercent(mx, my);

        c.SetZoom(1.8f, new Point(mx, my));

        Assert.Equal(1.8f, c.ZoomFactor);
        Assert.Equal(2880, c.CanvasArea.Width);
        var (ax, ay) = c.PixelToPercent(mx, my);
        Assert.InRange(ax, bx - 0.2f, bx + 0.2f);
        Assert.InRange(ay, by - 0.2f, by + 0.2f);
    }

    [Fact]
    public void SetZoom_AnchorInLetterboxBar_ClampsSoCanvasCoversClient()
    {
        using var c = NewCanvas(2000, 900);
        c.SetZoom(2.5f, new Point(0, 450));

        Assert.True(c.CanvasArea.Left <= 0 && c.CanvasArea.Top <= 0, c.CanvasArea.ToString());
        Assert.True(c.CanvasArea.Right >= 2000 && c.CanvasArea.Bottom >= 900, c.CanvasArea.ToString());
    }

    [Fact]
    public void SetZoom_ResetZoom_RestoresLetterboxCanvasArea()
    {
        using var c = NewCanvas(2000, 900);
        var fit = c.CanvasArea;

        c.SetZoom(2f, new Point(500, 500));
        Assert.NotEqual(fit, c.CanvasArea);

        c.ResetZoom();
        Assert.Equal(new Rectangle(200, 0, 1600, 900), c.CanvasArea);
        Assert.Equal(Point.Empty, c.PanOffset);
        Assert.Equal(1.0f, c.ZoomFactor);
    }

    private sealed class PanProbeCanvas : VacuumSchematicCanvas
    {
        public PanProbeCanvas() { InteractiveZoom = true; SetBounds(0, 0, 1600, 900); }
        public void Down(Point p) => OnMouseDown(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, p.X, p.Y, 0));
        public void Move(Point p) => OnMouseMove(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 0, p.X, p.Y, 0));
        public void Up(Point p) => OnMouseUp(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, p.X, p.Y, 0));
    }

    [Fact]
    public void Pan_SecondButtonPress_RestoresOriginalCursorOnRelease()
    {
        using var c = new PanProbeCanvas();
        var original = c.Cursor;
        c.SetZoom(2f);

        c.Down(new Point(800, 450));
        c.Down(new Point(800, 450));
        c.Up(new Point(800, 450));

        Assert.Equal(original, c.Cursor);
    }

    [Fact]
    public void Pan_ZoomDuringDrag_KeepsAnchoredViewOnNextMove()
    {
        using var c = new PanProbeCanvas();
        c.SetZoom(2f);
        var start = c.CanvasArea;
        c.Down(new Point(400, 300));
        var cursor = new Point(360, 280);
        c.Move(cursor);
        Assert.Equal(new Point(start.X - 40, start.Y - 20), c.CanvasArea.Location);

        c.SetZoom(2.3f, cursor);
        var afterZoom = c.CanvasArea;
        c.Move(cursor);

        Assert.Equal(afterZoom, c.CanvasArea);
        c.Up(cursor);
    }
}
