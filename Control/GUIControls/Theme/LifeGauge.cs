using System.Drawing;

namespace APS.SystemControl.Controls.Theme;

/// <summary>
/// Single source of truth for the erosion-life gauge palette and banding, shared by the
/// Targets inventory grid and the chamber source schematic. fraction = life consumed
/// (see APS.Production.Domain.ErosionMath.LifeFraction, clamped 0..1).
/// </summary>
public static class LifeGauge
{
    public static readonly Color Green = Color.FromArgb(129, 199, 132);
    public static readonly Color Amber = Color.FromArgb(255, 183, 77);
    public static readonly Color Red   = Color.FromArgb(239, 83, 80);

    public static Color ColorFor(double fraction, double warnFraction) =>
        fraction >= 1.0 ? Red : fraction >= warnFraction ? Amber : Green;
}
