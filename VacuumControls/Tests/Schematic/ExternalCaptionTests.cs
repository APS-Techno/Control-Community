using System.Drawing;
using System.Drawing.Imaging;
using APS.Controls.Vacuum;
using APS.Controls.Vacuum.Schematic;
using Xunit;

namespace APS.Controls.Tests.Schematic;

/// <summary>
/// Vertical valves suppress their in-frame ID text (extending the frame downward
/// would put the bottom anchor mid-frame and hide the pipe below) and expose it
/// via ThemedVacuumControl.ExternalCaption instead. The shared canvas base must
/// render that caption so BOTH hosts — Builder's designer and Control's runtime
/// scheme — show a label.
/// </summary>
public class ExternalCaptionTests
{
    private const int CanvasW = 800, CanvasH = 450;

    /// <summary>Canvas width at which CanvasScale == 1.0 (the authoring reference).</summary>
    private const int ReferenceW = 2200, ReferenceH = 1238;

    private sealed class CaptionCanvas : VacuumSchematicCanvas
    {
        public void PaintCaptions(Graphics g) => DrawExternalCaptions(g);

        public float CaptionFontHeight()
        {
            using var f = CreateCaptionFont();
            return f.GetHeight();
        }
    }

    /// <summary>Bounding box of pixels the caption pass painted, or null if it painted nothing.</summary>
    private static Rectangle? RenderCaptions(CaptionCanvas canvas, int w = CanvasW, int h = CanvasH)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            canvas.PaintCaptions(g);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (bmp.GetPixel(x, y).A > 0)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }

        if (maxX < 0) return null;
        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static CaptionCanvas NewCanvasWith(ValveControl valve, int w = CanvasW, int h = CanvasH)
    {
        var canvas = new CaptionCanvas();
        canvas.SetBounds(0, 0, w, h);
        canvas.AddComponent(valve, 50f, 50f);
        return canvas;
    }

    [Fact]
    public void VerticalValve_CaptionIsDrawnBelowTheChildFrame()
    {
        var valve = new ValveControl("GVM") { Direction = "vert" };
        var canvas = NewCanvasWith(valve);

        var drawn = RenderCaptions(canvas);

        Assert.NotNull(drawn);
        Assert.True(drawn!.Value.Top >= valve.Bottom,
            $"caption top {drawn.Value.Top} should be at or below child bottom {valve.Bottom}");

        canvas.Dispose();
    }

    [Fact]
    public void VerticalValve_CaptionClearsTheBottomAnchorSoThePipeStaysVisible()
    {
        var valve = new ValveControl("GVM") { Direction = "vert" };
        var canvas = NewCanvasWith(valve);
        int anchorX = valve.GetAnchorPoint("bottom").X;

        var drawn = RenderCaptions(canvas);

        Assert.NotNull(drawn);
        // Pipe drops straight down from the bottom anchor — glyphs must stay right of it.
        // 2 px tolerance for glyph antialiasing bleeding left of the layout origin.
        Assert.True(drawn!.Value.Left >= anchorX + 6 - 2,
            $"caption left {drawn.Value.Left} should clear bottom anchor x {anchorX}");

        canvas.Dispose();
    }

    [Fact]
    public void HorizontalValve_HasNoExternalCaption_ItPaintsItsOwnLabel()
    {
        var valve = new ValveControl("V1") { Direction = "hor" };
        var canvas = NewCanvasWith(valve);

        Assert.Null(valve.ExternalCaption);
        Assert.Null(RenderCaptions(canvas));

        canvas.Dispose();
    }

    /// <summary>
    /// Overrides nothing, like Control's runtime scheme: the base's own paint path must
    /// draw captions, before the subclass hook.
    /// </summary>
    private sealed class PlainHostCanvas : VacuumSchematicCanvas
    {
        public List<string> Order { get; } = new();

        protected override Font CreateCaptionFont()
        {
            Order.Add("Captions");
            return base.CreateCaptionFont();
        }

        protected override void OnPaintOverChildren(Graphics g) => Order.Add("OverChildren");

        public void SendWmPaint()
        {
            CreateControl();
            var m = Message.Create(Handle, 0x000F /* WM_PAINT */, IntPtr.Zero, IntPtr.Zero);
            typeof(Control).GetMethod("WndProc",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(this, new object[] { m });
        }
    }

    [Fact]
    public void HostThatOverridesNothing_StillDrawsExternalCaptions_BeforeItsOwnOverlay()
    {
        var canvas = new PlainHostCanvas();
        canvas.SetBounds(0, 0, CanvasW, CanvasH);
        canvas.AddComponent(new ValveControl("GVM") { Direction = "vert" }, 50f, 50f);

        canvas.SendWmPaint();

        Assert.Equal(new[] { "Captions", "OverChildren" }, canvas.Order);

        canvas.Dispose();
    }

    [Theory]
    [InlineData(1100, 619, false)]  // CanvasScale 0.5  — smaller than reference
    [InlineData(3840, 2160, true)]  // CanvasScale ≈1.75 (4K) — larger than reference
    public void CaptionFontScalesWithCanvasScale(int canvasW, int canvasH, bool expectLarger)
    {
        var reference = new CaptionCanvas();
        reference.SetBounds(0, 0, ReferenceW, ReferenceH);

        var scaled = new CaptionCanvas();
        scaled.SetBounds(0, 0, canvasW, canvasH);

        // Caption size must track CanvasScale, matching the sibling widgets' own
        // labels rather than staying a fixed pixel size on 4K panels.
        float refH = reference.CaptionFontHeight();
        float scaledH = scaled.CaptionFontHeight();

        if (expectLarger)
            Assert.True(scaledH > refH, $"expected {scaledH} > {refH} at CanvasScale {scaled.CanvasScale}");
        else
            Assert.True(scaledH < refH, $"expected {scaledH} < {refH} at CanvasScale {scaled.CanvasScale}");

        reference.Dispose();
        scaled.Dispose();
    }
}
