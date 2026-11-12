using System.Drawing;
using APS.Controls.Vacuum;
using Xunit;

namespace APS.Controls.Tests.Vacuum;

public class MagnetronControlTests
{
    [Fact]
    public void Energized_RoundTrips()
    {
        using var m = new MagnetronControl("PSU-A", "Ar");
        Assert.False(m.Energized);
        m.Energized = true;
        Assert.True(m.Energized);
        m.Energized = false;
        Assert.False(m.Energized);
    }

    [Fact]
    public void Render_WhenEnergized_DoesNotThrow()
    {
        using var m = new MagnetronControl("PSU-A", "Ar") { Energized = true };
        m.CreateControl();

        int w = System.Math.Max(1, m.Width);
        int h = System.Math.Max(1, m.Height);
        using var bmp = new Bitmap(w, h);

        var ex = Record.Exception(() =>
            m.DrawToBitmap(bmp, new Rectangle(0, 0, w, h)));

        Assert.Null(ex);
    }
}
