namespace APS.SystemControl.Controls.Theme;

/// <summary>
/// Despite the name, fonts are created at designer-intent point size and not
/// shrunk: shrinking captions below designer intent makes them unreadable.
/// WinForms handles per-monitor DPI scaling for actual font rendering via
/// ApplicationHighDpiMode=SystemAware.
/// </summary>
public static class ScaledFont
{
    public static Font Create(float ptSize, FontStyle style = FontStyle.Regular)
        => new("Segoe UI", ptSize, style);
}
