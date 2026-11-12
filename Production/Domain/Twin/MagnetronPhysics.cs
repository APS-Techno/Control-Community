using System;
using System.Text.Json;

namespace APS.Production.Domain;

/// <summary>Versioned schema for Magnetron.PhysicsJson: the racetrack geometry (config) plus the
/// runtime health counters. Tolerant parse — any null/blank/invalid blob yields safe defaults.</summary>
public sealed class MagnetronPhysics
{
    public int Version { get; set; } = 1;
    public RacetrackShape Racetrack { get; set; } = new();
    public MagnetronHealth Health { get; set; } = new();

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Opts);

    public static MagnetronPhysics FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new MagnetronPhysics();
        try { return JsonSerializer.Deserialize<MagnetronPhysics>(json, Opts) ?? new MagnetronPhysics(); }
        catch (JsonException) { return new MagnetronPhysics(); }
    }
}

public sealed class MagnetronHealth
{
    public double CumulativeKWh { get; set; }
    public double HotHours { get; set; }
    public int PowerCycles { get; set; }
    public ServiceLimits ServiceLimits { get; set; } = new();
}

public sealed class ServiceLimits
{
    public double KWh { get; set; }
    public double HotHours { get; set; }
    public int PowerCycles { get; set; }
}

public enum AssetStatus { Ok, Warn, Due, Over }

public static class AssetHealth
{
    public static AssetStatus StatusFor(double fraction, double warnFraction)
        => fraction >= 1.10 ? AssetStatus.Over
         : fraction >= 1.00 ? AssetStatus.Due
         : fraction >= warnFraction ? AssetStatus.Warn
         : AssetStatus.Ok;

    /// <summary>Worst (max) fraction across the three service limits; 0.8 warn band.</summary>
    public static AssetStatus ServiceStatus(MagnetronHealth h)
    {
        double f = 0;
        if (h.ServiceLimits.KWh > 0)        f = Math.Max(f, h.CumulativeKWh / h.ServiceLimits.KWh);
        if (h.ServiceLimits.HotHours > 0)   f = Math.Max(f, h.HotHours / h.ServiceLimits.HotHours);
        if (h.ServiceLimits.PowerCycles > 0) f = Math.Max(f, (double)h.PowerCycles / h.ServiceLimits.PowerCycles);
        return StatusFor(f, 0.8);
    }
}
