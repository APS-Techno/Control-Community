using System;

namespace APS.Production.Domain;

/// <summary>Pure, driver-agnostic stepper for a magnetron+target twin. No ambient time/IO.</summary>
public static class MagnetronTwin
{
    /// <summary>Advance the groove by one tick. The kWh for this tick is distributed across the
    /// radial bins by the racetrack weight, then converted to per-bin depth via the annulus area —
    /// so the narrow, energy-dense ring grinds deep. Calibrated so the fastest-eroding bin advances
    /// at exactly <paramref name="rateMmPerKWh"/> per kWh (the target's existing calibration).</summary>
    public static void Step(ErosionProfile p, RacetrackShape shape, double rateMmPerKWh, double watts, double dtSeconds)
    {
        if (watts <= 0 || dtSeconds <= 0 || p.N <= 0 || rateMmPerKWh <= 0) return;

        double e = ErosionMath.EnergyKWh(watts, dtSeconds);
        double[] w = shape.Weights(p);

        // Specific rate of each bin = w[i]/area[i]; the max defines the deepest bin.
        double best = 0;
        for (int i = 0; i < p.N; i++)
        {
            double a = p.BinAreaMm2(i);
            if (a > 0) best = Math.Max(best, w[i] / a);
        }
        if (best <= 0) return;

        // Cmat * best = rateMmPerKWh  =>  Cmat = rate / best.
        double cmat = rateMmPerKWh / best;
        for (int i = 0; i < p.N; i++)
        {
            double a = p.BinAreaMm2(i);
            if (a > 0) p.DepthMm[i] += cmat * e * w[i] / a;
        }
    }

    /// <summary>Scale the whole groove so its deepest point matches an operator measurement (the
    /// shape comes from the model, the scale from the measurement). Records the anchor depth; the
    /// caller stamps the time.</summary>
    public static void ReAnchor(ErosionProfile p, double measuredDeepestMm)
    {
        double cur = Deepest(p);
        if (cur > 0 && measuredDeepestMm >= 0)
        {
            double s = measuredDeepestMm / cur;
            for (int i = 0; i < p.N; i++) p.DepthMm[i] *= s;
        }
        else if (cur <= 0 && measuredDeepestMm > 0)
        {
            // Fresh/all-zero profile: no shape to scale, so flat-seed every bin to the
            // measured depth — otherwise the measurement is lost (bins stay 0).
            for (int i = 0; i < p.N; i++) p.DepthMm[i] = measuredDeepestMm;
        }
        p.LastAnchorDepthMm = measuredDeepestMm;
    }

    public static TwinReadout Summarize(ErosionProfile p, double maxErosionDepthMm)
    {
        double deepest = Deepest(p);
        double face = Math.PI * p.TargetRadiusMm * p.TargetRadiusMm;
        double vol = 0;
        for (int i = 0; i < p.N; i++) vol += p.DepthMm[i] * p.BinAreaMm2(i);
        double util = (maxErosionDepthMm > 0 && face > 0) ? vol / (maxErosionDepthMm * face) : 0;
        return new TwinReadout(deepest, 100.0 * Math.Clamp(util, 0.0, 1.0));
    }

    /// <summary>Hours until the deepest bin reaches <paramref name="maxMm"/> at the present power.
    /// (The deepest bin advances at exactly rateMmPerKWh per kWh by construction — see Step.)</summary>
    public static double BurnThroughHours(double deepestMm, double maxMm, double rateMmPerKWh, double currentWatts)
    {
        double remaining = maxMm - deepestMm;
        if (remaining <= 0) return 0;
        double mmPerHour = rateMmPerKWh * (currentWatts / 1000.0); // W -> kWh/h -> mm/h
        return mmPerHour > 0 ? remaining / mmPerHour : double.PositiveInfinity;
    }

    private static double Deepest(ErosionProfile p)
    {
        double m = 0;
        for (int i = 0; i < p.N; i++) if (p.DepthMm[i] > m) m = p.DepthMm[i];
        return m;
    }
}

public readonly record struct TwinReadout(double DeepestMm, double UtilizationPct);
