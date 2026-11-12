namespace APS.Controls.Theme;

/// <summary>
/// Process-wide DPI factor for raw-pixel layout in runtime-built dialogs and
/// dynamically generated content. Layout pixels in this codebase are 96-DPI
/// logical values; every host runs ApplicationHighDpiMode=SystemAware, so the
/// factor is fixed for the process lifetime (no per-monitor rescale to track).
/// Point-sized fonts scale with DPI on their own — only raw pixel values need
/// this. Same convention as RecipeModel's BlockTheme.Scale.
/// </summary>
public static class DpiScale
{
    public static float Factor { get; } = ReadFactor();

    private static float ReadFactor()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiY / 96f;
    }

    public static int Px(int px) => (int)Math.Round(px * Factor);
}
