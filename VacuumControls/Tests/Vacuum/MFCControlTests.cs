using System.Drawing;
using APS.Controls.Tests.Rendering;
using APS.Controls.Vacuum;
using Xunit;

namespace APS.Controls.Tests.Vacuum;

public class MFCControlTests
{
    [Theory]
    [InlineData(0.45f, 0.9f, false)]
    [InlineData(0.65f, 1.0f, true)]
    public void CaptionAndValue_StayInsideScreen_ClearOfGasStrip(float baseScale, float vScale, bool captionFits)
    {
        using var mfc = new MFCControl("MFC1", "N2")
        {
            BaseScale = baseScale,
            VScale = vScale,
            Caption = "sccm",
            Value = "8.88",
            FontColor = Color.Magenta,
        };
        var L = MFCControl.ComputeLayout(mfc.Width, mfc.Height, baseScale * vScale, hasCaption: true);

        Assert.False(L.Lcd.IntersectsWith(L.GasStrip));
        Assert.Equal(captionFits, L.ShowsCaption);

        using var bmp = InkProbe.Render(mfc);

        var value = InkProbe.Bounds(bmp, c => c.R >= 100 && c.B >= 100 && c.G < Math.Min(c.R, c.B) / 2);
        Assert.NotNull(value);
        Assert.True(InkProbe.StrictlyInside(value.Value, L.Screen), $"value ink {value} vs screen {L.Screen}");
        Assert.True(value.Value.Top >= L.ValueBand.Top - 1 && value.Value.Bottom <= L.ValueBand.Bottom + 1,
            $"value ink {value} vs band {L.ValueBand}");

        var caption = InkProbe.Bounds(bmp, c => c.G >= 80 && c.R < 50 && c.G > c.B + 20 && c.B > c.R + 20);
        Assert.Equal(captionFits, caption.HasValue);
        if (caption is { } cap)
        {
            Assert.True(InkProbe.StrictlyInside(cap, L.Screen), $"caption ink {cap} vs screen {L.Screen}");
            Assert.True(cap.Bottom <= L.CaptionBand.Bottom + 1, $"caption ink {cap} vs band {L.CaptionBand}");
            Assert.True(cap.Bottom <= value.Value.Top, $"caption ink {cap} overlaps value ink {value}");
        }
    }
}
