using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace APS.Icons;

public static class IconLibrary
{
    private static readonly int[] AvailableSizes = { 16, 24, 32, 48 };
    private static readonly Assembly Asm = typeof(IconLibrary).Assembly;

    public static Bitmap Get(string name, int size)
    {
        var key = name.ToLowerInvariant();
        var srcSize = ChooseSize(size);
        // MSBuild prepends '_' to path segments starting with a digit.
        var resName = $"APS.Icons.png._{srcSize}.{key}.png";

        using var stream = Asm.GetManifestResourceStream(resName)
            ?? throw new KeyNotFoundException(
                $"Icon '{name}' not found. Expected embedded resource: {resName}");

        var src = new Bitmap(stream);
        if (src.Width == size && src.Height == size) return src;

        var dst = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, size, size);
        }
        src.Dispose();
        return dst;
    }

    public static ImageList BuildImageList(int size, params string[] names)
    {
        var list = new ImageList { ImageSize = new Size(size, size), ColorDepth = ColorDepth.Depth32Bit };
        // NOTE: do not dispose the bitmap after Add — ImageList keeps the original and only
        // copies it into the native HIMAGELIST when the handle is first created (lazily).
        foreach (var n in names)
            list.Images.Add(n.ToLowerInvariant(), Get(n, size));
        return list;
    }

    /// <summary>
    /// WinForms does NOT DPI-scale ImageScalingSize under SystemAware mode, so callers pass an
    /// already-scaled size (e.g. <c>DpiScale.Px(24)</c>). The list is returned for places that need
    /// a detached bitmap: drop-down menu items do not resolve ImageKey through the parent MenuStrip,
    /// and an explicit Image always wins over ImageKey, so a designer-created item must have its
    /// resource Image nulled first.
    /// </summary>
    public static ImageList ConfigureToolStrip(ToolStrip strip, int sizePx, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(strip);
        var list = BuildImageList(sizePx, names);
        strip.ImageScalingSize = new Size(sizePx, sizePx);
        strip.ImageList = list;
        strip.Disposed += (_, _) => list.Dispose();
        return list;
    }

    private static int ChooseSize(int requested)
    {
        foreach (var s in AvailableSizes)
            if (s >= requested) return s;
        return AvailableSizes[^1];
    }
}
