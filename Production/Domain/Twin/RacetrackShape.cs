using System;

namespace APS.Production.Domain;

/// <summary>Where a magnetron grinds: a normalized radial energy distribution. A magnetron-model
/// property (lives in Magnetron.PhysicsJson). "uniform" spreads energy by area (areal-uniform =
/// the legacy uniform-depth bridge); otherwise a Gaussian ring centered at PeakRadiusMm.</summary>
public sealed class RacetrackShape
{
    public double PeakRadiusMm { get; set; }
    public double WidthMm { get; set; } = 1.0;
    public string Shape { get; set; } = "gaussian";

    /// <summary>Per-bin weights over the profile's N bins; always sums to 1 (zero-safe fallback: uniform-by-area).</summary>
    public double[] Weights(ErosionProfile p)
    {
        int n = p.N;
        var w = new double[n];
        bool areal = string.Equals(Shape, "uniform", StringComparison.OrdinalIgnoreCase) || WidthMm <= 0;

        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            if (areal)
                w[i] = p.BinAreaMm2(i);
            else
            {
                double dr = p.BinMidRadiusMm(i) - PeakRadiusMm;
                w[i] = Math.Exp(-(dr * dr) / (2.0 * WidthMm * WidthMm)) * p.BinAreaMm2(i);
            }
            sum += w[i];
        }
        if (sum <= 0) { for (int i = 0; i < n; i++) w[i] = p.BinAreaMm2(i); sum = Math.PI * p.TargetRadiusMm * p.TargetRadiusMm; }
        for (int i = 0; i < n; i++) w[i] /= sum;
        return w;
    }

    /// <summary>A believable default racetrack ring for a target of the given radius — used when a
    /// magnetron has no configured racetrack. Peak at ~65% of the radius, ring width ~12%.</summary>
    public static RacetrackShape DefaultRing(double targetRadiusMm) => new()
    {
        PeakRadiusMm = 0.65 * targetRadiusMm,
        WidthMm      = Math.Max(0.5, 0.12 * targetRadiusMm),
        Shape        = "gaussian",
    };

    /// <summary>Return a racetrack usable for a target of this radius. An explicit "uniform" shape is
    /// kept as-is (intentionally flat). A gaussian with no usable peak/width (the unconfigured 0/1
    /// default, which would spike at the center) falls back to <see cref="DefaultRing"/>.</summary>
    public RacetrackShape ResolveForRadius(double targetRadiusMm)
    {
        if (string.Equals(Shape, "uniform", StringComparison.OrdinalIgnoreCase)) return this;
        if (PeakRadiusMm > 0 && WidthMm > 0) return this;
        return DefaultRing(targetRadiusMm);
    }
}
