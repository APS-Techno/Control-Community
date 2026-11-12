namespace APS.SystemControl.Core.Classes;

public class Source
{
    public int FlangeNumber { get; set; }
    // Cluster systems only: the chamber this source sits in + its per-source shutter.
    // Null for single-chamber systems (Sputter). Round-tripped through Sources.xml so
    // editing/mounting from the Sources form never strips a cluster's chamber/shutter map.
    public string? Chamber { get; set; }
    public string? ShutterId { get; set; }
    // Optional absolute placement angle (deg, 0 = top) for the source schematic. Null =
    // take the auto-computed even-ring slot. Round-tripped via the <source angle> attribute.
    public double? AngleDegrees { get; set; }
    /// <summary>Operator-facing, globally-unique source handle used by recipes
    /// (deposit/holder/shutter). Empty = fall back to Material as the handle.</summary>
    public string Name { get; set; } = "";
    public string Material { get; set; } = "";
    public string Model { get; set; } = "";
    public string PowerType { get; set; } = "";
    public string MagnetronId { get; set; } = "";
    public string MagnetronInstanceId { get; set; } = "";   // -> a MagnetronInstance.Id (the physical cathode here)
    public string PowerSupplyId { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime InstalledDate { get; set; } = DateTime.Now;
    public string Comment { get; set; } = "";
    public TimeSpan TotalDepositionTime { get; set; } = TimeSpan.Zero;
    public TimeSpan CurrentRunTime { get; set; } = TimeSpan.Zero;

    /// <summary>Bare magnetron-face-to-substrate distance measured at install (lift gauge = 0), mm. Null = unset.</summary>
    public double? ZeroDistanceMm { get; set; }
    /// <summary>Current lift gauge reading, mm; lifting the magnetron raises the target away from
    /// the substrate below, so it grows the gap. Null = unset.</summary>
    public double? LiftPositionMm { get; set; }

    private DateTime _runStart;
    private bool _isRunning;

    public void StartDeposition()
    {
        _runStart = DateTime.Now;
        _isRunning = true;
    }

    public void StopDeposition()
    {
        if (_isRunning)
        {
            CurrentRunTime = DateTime.Now - _runStart;
            TotalDepositionTime += CurrentRunTime;
            _isRunning = false;
        }
    }

    public void ResetRunTime()
    {
        CurrentRunTime = TimeSpan.Zero;
    }
}
