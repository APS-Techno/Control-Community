namespace APS.Production.Domain;

/// <summary>
/// Pure erosion-depth math. Depth is the single source of truth (mm); energy (kWh)
/// integrated from actual source power advances it from the last operator-measured anchor.
/// </summary>
public static class ErosionMath
{
    /// <summary>Energy in kWh from average power (W) held over a span (seconds).</summary>
    public static double EnergyKWh(double watts, double seconds) => watts * seconds / 3_600_000.0;

    public static double CurrentDepthMm(double anchorDepthMm, double rateMmPerKWh, double energyKWh) =>
        anchorDepthMm + rateMmPerKWh * energyKWh;

    /// <summary>Fraction of usable life consumed, clamped to [0, 1]. Zero max returns 0.</summary>
    public static double LifeFraction(double currentDepthMm, double maxDepthMm)
    {
        if (maxDepthMm <= 0) return 0.0;
        var f = currentDepthMm / maxDepthMm;
        return f < 0 ? 0 : (f > 1 ? 1 : f);
    }
}
