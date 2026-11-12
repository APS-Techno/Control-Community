using System.Drawing;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BlockPaletteTooltipTests
{
    [StaFact]
    public void Measure_Returns_Size_Big_Enough_For_Two_Lines()
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        var size = BlockPalette.MeasureTooltip("valve ID open|close\nValve control", g);

        Assert.True(size.Width  > 50,
            $"expected non-trivial width, got {size.Width}");
        Assert.True(size.Height > 20,
            $"expected room for two lines + padding, got {size.Height}");
    }

    [StaFact]
    public void Measure_Survives_Single_Line_Without_Newline()
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        var size = BlockPalette.MeasureTooltip("abc", g);

        Assert.True(size.Width  > 0);
        Assert.True(size.Height > 0);
    }

    [StaFact]
    public void Draw_Renders_Without_Throwing()
    {
        using var bmp = new Bitmap(200, 40);
        using var g = Graphics.FromImage(bmp);

        BlockPalette.DrawTooltip(g, new Rectangle(0, 0, 200, 40), "abc\ndef");
    }
}
