using System.Drawing;

namespace APS.Controls.Tests.Rendering;

internal static class InkProbe
{
    public static Bitmap Render(Control c)
    {
        c.CreateControl();
        var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height));
        c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        return bmp;
    }

    public static Rectangle? Bounds(Bitmap bmp, Func<Color, bool> isInk)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            if (!isInk(bmp.GetPixel(x, y))) continue;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
        return maxX < 0 ? null : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    public static bool StrictlyInside(Rectangle inner, Rectangle outer) =>
        inner.Left > outer.Left && inner.Top > outer.Top
        && inner.Right < outer.Right && inner.Bottom < outer.Bottom;
}
