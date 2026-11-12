using System.Drawing.Drawing2D;
using System.Xml.Linq;

namespace APS.Controls.Theme;

/// <summary>
/// All factory methods return caller-owned objects — wrap in using blocks.
/// </summary>
public static class VacuumTheme
{
    public static Color MetalHighlight { get; private set; } = Color.FromArgb(106, 106, 126);
    public static Color MetalMid { get; private set; } = Color.FromArgb(58, 58, 78);
    public static Color MetalDark { get; private set; } = Color.FromArgb(30, 30, 48);
    public static Color MetalEdge { get; private set; } = Color.FromArgb(85, 85, 104);

    public static Color GlowGreen { get; private set; } = Color.FromArgb(0, 150, 80);
    public static Color GlowRed { get; private set; } = Color.FromArgb(255, 68, 102);
    public static Color GlowBlue { get; private set; } = Color.FromArgb(68, 136, 255);
    public static Color GlowYellow { get; private set; } = Color.FromArgb(255, 221, 51);
    public static Color GlowAmber { get; private set; } = Color.FromArgb(255, 176, 32);
    public static Color GlowPlasma { get; private set; } = Color.FromArgb(200, 90, 255);

    public static Color CavityDark { get; private set; } = Color.FromArgb(8, 8, 26);
    public static Color BackgroundDark { get; private set; } = Color.FromArgb(10, 10, 26);
    public static Color LcdBackground { get; private set; } = Color.FromArgb(10, 26, 10);
    public static Color LcdForeground { get; private set; } = Color.FromArgb(0, 255, 136);

    public static Color ScrewFill { get; private set; } = Color.FromArgb(42, 42, 62);
    public static Color ScrewBorder { get; private set; } = Color.FromArgb(68, 68, 68);
    public static Color BorderPrimary { get; private set; } = Color.FromArgb(74, 74, 94);
    public static Color BorderSecondary { get; private set; } = Color.FromArgb(34, 34, 50);

    public static Color TextPrimary { get; private set; } = Color.FromArgb(200, 214, 229);
    public static Color TextDim { get; private set; } = Color.FromArgb(119, 136, 153);

    // Shared by configuration forms (frmSources) and process panels so every surface uses the same ink.
    public static Color PanelBackground { get; private set; } = Color.FromArgb(13, 27, 42);
    public static Color PanelBorder { get; private set; } = Color.FromArgb(40, 60, 90);
    public static Color PanelField { get; private set; } = Color.FromArgb(26, 42, 58);

    /// <summary>
    /// Explicit dark-mode override. When non-null, <see cref="IsDark"/> returns
    /// this value and ignores the .NET color-mode APIs entirely. Apps that are
    /// always dark (Emulator, Control) set this <c>true</c> at startup.
    /// <para>
    /// Why: the .NET 9 color-mode APIs are unreliable here. On a light-themed
    /// Windows machine, <c>Application.SystemColorMode</c> returns the OS theme
    /// (Classic), and <c>Application.ColorMode</c> was observed NOT to return
    /// Dark even after <c>SetColorMode(Dark)</c> — so auto-detection left forms
    /// half-themed (dark chrome, white grids/buttons). Forcing removes the guesswork.
    /// </para>
    /// </summary>
    public static bool? ForceDark { get; set; }

#pragma warning disable WFO5001
    /// <summary>True when the app is rendering its dark theme. Honors
    /// <see cref="ForceDark"/> first; otherwise falls back to the app's chosen
    /// color mode (<c>Application.ColorMode</c>), then the OS theme.</summary>
    public static bool IsDark =>
        ForceDark
        ?? (Application.ColorMode == SystemColorMode.Dark
            || (Application.ColorMode == SystemColorMode.System
                && Application.SystemColorMode == SystemColorMode.Dark));
#pragma warning restore WFO5001

    /// <summary>
    /// Call once at startup before any control is created. Loads optional Palette.xml
    /// overrides from the active profile directory; a null directory, a missing file
    /// or any error keeps the defaults.
    /// </summary>
    public static void LoadPalette(string? profileDir)
    {
        if (string.IsNullOrEmpty(profileDir)) return;
        var palettePath = Path.Combine(profileDir, "Palette.xml");
        if (!File.Exists(palettePath)) return;

        try
        {
            var doc = XDocument.Load(palettePath);
            var root = doc.Root;
            if (root == null) return;

            foreach (var el in root.Elements())
            {
                var name = el.Name.LocalName;
                var value = el.Value.Trim();
                if (TryParseColor(value, out var color))
                    SetColorByName(name, color);
            }
        }
        catch
        {
            // Malformed XML — silently keep defaults
        }
    }

    private static bool TryParseColor(string value, out Color color)
    {
        color = Color.Empty;
        if (value.StartsWith('#') && value.Length == 7)
        {
            try
            {
                color = ColorTranslator.FromHtml(value);
                return true;
            }
            catch { return false; }
        }

        var parts = value.Split(',');
        if (parts.Length == 3 &&
            int.TryParse(parts[0].Trim(), out int r) &&
            int.TryParse(parts[1].Trim(), out int g) &&
            int.TryParse(parts[2].Trim(), out int b))
        {
            color = Color.FromArgb(r, g, b);
            return true;
        }
        return false;
    }

    private static void SetColorByName(string name, Color color)
    {
        switch (name)
        {
            case nameof(MetalHighlight): MetalHighlight = color; break;
            case nameof(MetalMid): MetalMid = color; break;
            case nameof(MetalDark): MetalDark = color; break;
            case nameof(MetalEdge): MetalEdge = color; break;
            case nameof(GlowGreen): GlowGreen = color; break;
            case nameof(GlowRed): GlowRed = color; break;
            case nameof(GlowBlue): GlowBlue = color; break;
            case nameof(GlowYellow): GlowYellow = color; break;
            case nameof(GlowAmber): GlowAmber = color; break;
            case nameof(GlowPlasma): GlowPlasma = color; break;
            case nameof(CavityDark): CavityDark = color; break;
            case nameof(BackgroundDark): BackgroundDark = color; break;
            case nameof(LcdBackground): LcdBackground = color; break;
            case nameof(LcdForeground): LcdForeground = color; break;
            case nameof(ScrewFill): ScrewFill = color; break;
            case nameof(ScrewBorder): ScrewBorder = color; break;
            case nameof(BorderPrimary): BorderPrimary = color; break;
            case nameof(BorderSecondary): BorderSecondary = color; break;
            case nameof(TextPrimary): TextPrimary = color; break;
            case nameof(TextDim): TextDim = color; break;
            case nameof(PanelBackground): PanelBackground = color; break;
            case nameof(PanelBorder): PanelBorder = color; break;
            case nameof(PanelField): PanelField = color; break;
        }
    }

    public static LinearGradientBrush CreateMetalGradient(Rectangle bounds)
    {
        var brush = new LinearGradientBrush(bounds, MetalHighlight, MetalDark, LinearGradientMode.Vertical);
        var blend = new ColorBlend(6)
        {
            Colors = [
                Color.FromArgb(90, 90, 110),
                MetalHighlight,
                Color.FromArgb(78, 78, 96),
                MetalMid,
                Color.FromArgb(30, 30, 48),
                Color.FromArgb(20, 20, 37)
            ],
            Positions = [0f, 0.08f, 0.15f, 0.4f, 0.85f, 1f]
        };
        brush.InterpolationColors = blend;
        return brush;
    }

    public static LinearGradientBrush CreateEdgeHighlight(Rectangle bounds)
    {
        return new LinearGradientBrush(bounds,
            Color.FromArgb(153, MetalHighlight),
            Color.FromArgb(25, MetalDark),
            LinearGradientMode.Vertical);
    }

    public static PathGradientBrush CreateCavityGradient(Rectangle bounds)
    {
        using var path = new GraphicsPath();
        path.AddRectangle(bounds);
        var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(26, 26, 48),
            SurroundColors = [CavityDark],
            CenterPoint = new PointF(bounds.X + bounds.Width * 0.5f, bounds.Y + bounds.Height * 0.4f)
        };
        return brush;
    }

    public static SolidBrush CreateGlowBrush(Color baseColor, float intensity)
    {
        int alpha = (int)(intensity * 45);
        alpha = Math.Clamp(alpha, 0, 45);
        return new SolidBrush(Color.FromArgb(alpha, baseColor));
    }

    public static LinearGradientBrush CreateLcdBackground(Rectangle bounds)
    {
        return new LinearGradientBrush(bounds,
            LcdBackground,
            Color.FromArgb(3, 10, 3),
            LinearGradientMode.Vertical);
    }

    public static Color LerpColor(Color from, Color to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(from.A + (to.A - from.A) * t),
            (int)(from.R + (to.R - from.R) * t),
            (int)(from.G + (to.G - from.G) * t),
            (int)(from.B + (to.B - from.B) * t));
    }
}
