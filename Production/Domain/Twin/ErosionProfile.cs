using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APS.Production.Domain;

/// <summary>1-D radial erosion groove on one target: depth(r) over N equal-width annular bins
/// from center (bin 0) to rim (bin N-1). Pure, serializable state — travels with the target.</summary>
public sealed class ErosionProfile
{
    public int Version { get; set; } = 1;
    public int N { get; set; }
    public double TargetRadiusMm { get; set; }
    public double[] DepthMm { get; set; } = Array.Empty<double>();
    public double LastAnchorDepthMm { get; set; }
    public DateTime? LastAnchorUtc { get; set; }

    [JsonIgnore] public double BinWidthMm => N > 0 ? TargetRadiusMm / N : 0.0;
    public double BinMidRadiusMm(int i) => (i + 0.5) * BinWidthMm;

    public double BinAreaMm2(int i)
    {
        double w = BinWidthMm, r0 = i * w, r1 = (i + 1) * w;
        return Math.PI * (r1 * r1 - r0 * r0);
    }

    /// <summary>A fresh groove of uniform depth (back-compat seed from a legacy scalar depth).</summary>
    public static ErosionProfile SeedFlat(int n, double targetRadiusMm, double uniformDepthMm)
    {
        var d = new double[n];
        for (int i = 0; i < n; i++) d[i] = uniformDepthMm;
        return new ErosionProfile { N = n, TargetRadiusMm = targetRadiusMm, DepthMm = d };
    }

    /// <summary>Seed a groove whose radial shape matches the magnetron's racetrack — the same
    /// per-bin specific-rate distribution that live <see cref="MagnetronTwin.Step"/> accrual would
    /// carve — scaled so the deepest bin equals <paramref name="deepestMm"/>. This lets a target
    /// that has only a measured scalar depth (no live-accrued profile yet) still display the
    /// expected gaussian groove, and makes the volume-derived utilization physically meaningful.
    /// A "uniform" shape reduces to the flat groove of <see cref="SeedFlat"/>.</summary>
    public static ErosionProfile SeedShaped(int n, double targetRadiusMm, RacetrackShape shape, double deepestMm)
    {
        var p = new ErosionProfile { N = n, TargetRadiusMm = targetRadiusMm, DepthMm = new double[n] };
        if (n <= 0) return p;

        // Per-bin specific rate = weight/area (the area cancels in Step, leaving the bare gaussian).
        // The deepest bin sets the scale, so depth[i] = deepestMm * spec[i] / max(spec).
        double[] w = shape.Weights(p);
        double best = 0;
        var spec = new double[n];
        for (int i = 0; i < n; i++)
        {
            double a = p.BinAreaMm2(i);
            spec[i] = a > 0 ? w[i] / a : 0;
            if (spec[i] > best) best = spec[i];
        }
        if (best <= 0) { for (int i = 0; i < n; i++) p.DepthMm[i] = deepestMm; return p; }
        for (int i = 0; i < n; i++) p.DepthMm[i] = deepestMm * spec[i] / best;
        return p;
    }

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Opts);
    public static ErosionProfile FromJson(string json) =>
        JsonSerializer.Deserialize<ErosionProfile>(json, Opts) ?? new ErosionProfile();
}
