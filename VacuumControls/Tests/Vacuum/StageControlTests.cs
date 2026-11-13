using System.Drawing;
using APS.Controls.Tests.Rendering;
using APS.Controls.Vacuum;
using Xunit;

namespace APS.Controls.Tests.Vacuum;

public class StageControlTests
{
    public static TheoryData<float, float> SubReferenceScales => new()
    {
        { 0.45f, 0.9f },
        { 0.65f, 1.0f },
    };

    [Theory]
    [MemberData(nameof(SubReferenceScales))]
    public void Lcd_KeepsRoomBetweenLabelStripAndButtons(float baseScale, float vScale)
    {
        using var stage = new StageControl("ST1") { BaseScale = baseScale, VScale = vScale };
        var L = StageControl.ComputeLayout(stage.Width, stage.Height, baseScale * vScale);
        var buttons = stage.Controls.OfType<Button>().Select(b => b.Bounds).ToArray();

        Assert.True(L.Lcd.Height >= 10, $"LCD height {L.Lcd.Height} at {stage.Size}");
        Assert.Contains(L.Move, buttons);
        Assert.Contains(L.Stop, buttons);
        Assert.False(L.Label.IntersectsWith(L.Lcd));
        foreach (var b in buttons)
        {
            Assert.False(b.IntersectsWith(L.Lcd), $"button {b} overlaps LCD {L.Lcd}");
            Assert.False(b.IntersectsWith(L.Label), $"button {b} overlaps label {L.Label}");
            Assert.True(stage.ClientRectangle.Contains(b), $"button {b} leaves {stage.ClientRectangle}");
        }
    }

    [Theory]
    [MemberData(nameof(SubReferenceScales))]
    public void Value_IsPaintedWhole_InsideLcdScreen(float baseScale, float vScale)
    {
        using var stage = new StageControl("ST1") { BaseScale = baseScale, VScale = vScale, StageHeight = 88.8f };
        var L = StageControl.ComputeLayout(stage.Width, stage.Height, baseScale * vScale);

        using var bmp = InkProbe.Render(stage);
        var ink = InkProbe.Bounds(bmp, c => c.G > 100 && c.G > c.R + 60 && c.G > c.B + 30);

        Assert.NotNull(ink);
        Assert.True(InkProbe.StrictlyInside(ink.Value, L.Screen), $"value ink {ink} vs screen {L.Screen}");
    }
}
