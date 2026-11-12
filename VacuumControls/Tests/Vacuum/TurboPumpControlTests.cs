using APS.Controls.Vacuum;
using Xunit;

namespace APS.Controls.Tests.Vacuum;

public class TurboPumpControlTests
{
    [Fact]
    public void Render_TinyScale_DoesNotThrow()
    {
        // A very small VScale makes the tube only a few pixels; the cavity inset
        // then exceeds the tube, producing a non-positive rectangle. Painting must
        // not throw (regression: ArgumentException from GraphicsPath.AddArc).
        using var p = new TurboPumpControl("LL_TP") { VScale = 0.05f };
        p.CreateControl();

        int w = System.Math.Max(1, p.Width);
        int h = System.Math.Max(1, p.Height);
        using var bmp = new System.Drawing.Bitmap(w, h);

        var ex = Record.Exception(() =>
            p.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, w, h)));

        Assert.Null(ex);
    }
}
