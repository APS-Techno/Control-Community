namespace APS.ProcessPanel.Contracts;

/// <summary>One UI-tick snapshot of the whole cluster, pushed to the panel.</summary>
public sealed record ClusterSnapshot(
    ClusterChamberInfo[] Chambers,
    ClusterRobotInfo Robot);

/// <summary>Per-chamber state for one radial tile + the aggregated side-panel detail.</summary>
public sealed record ClusterChamberInfo(
    string Key,             // "LL","C1".."C4" (matches robot face name)
    int FaceAngle,          // degrees clockwise from 12 o'clock; from BaseRobot.FaceAngleMap
    SystemStateCode State,
    double? Pressure,       // C<n>_pm, Torr; null = no sensor / offline
    bool SlitOpen,          // SV_<Key> open state
    double? Temperature = null,             // C<n>_tmp, °C
    ClusterReadingInfo[]? Flows = null,     // C<n>_Ar, C<n>_N2, … sccm (all MFCs for the chamber)
    ClusterReadingInfo[]? Powers = null,    // C<n>_RF, C<n>_RF_B, … W (all power supplies for the chamber)
    ClusterValveInfo[]? Valves = null,      // C<n>_V/_FL/_RL/_A1/_GV (slit handled by SlitOpen)
    double? DrivePosition = null,           // C<n>_VS substrate lift position
    bool TurboOn = false,                   // C<n>_TP
    bool ScrollOn = false,                  // C<n>_SP
    ClusterSourceInfo[]? Sources = null);

/// <summary>One named live reading (an MFC flow or a power-supply output) for a
/// chamber. <see cref="Label"/> is the short display name (e.g. "Ar", "N2", "RF",
/// "RF B"); <see cref="Id"/> is the underlying device key (e.g. "C3_N2").</summary>
public sealed record ClusterReadingInfo(string Id, string Label, double? Value)
{
    /// <summary>
    /// Build a reading for device <paramref name="key"/> in <paramref name="chamber"/>,
    /// deriving the short <see cref="Label"/> from the key: strip the leading
    /// "&lt;chamber&gt;_" prefix and turn remaining underscores into spaces
    /// (("C3_N2","C3")→"N2"; ("C1_RF_B","C1")→"RF B"). Falls back to the raw key
    /// when the prefix does not match.
    /// </summary>
    public static ClusterReadingInfo For(string key, string chamber, double? value)
    {
        string prefix = chamber + "_";
        string body = key.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)
            ? key[prefix.Length..]
            : key;
        return new ClusterReadingInfo(key, body.Replace('_', ' '), value);
    }
}

/// <summary>Transfer-module + robot state for the centre hex.</summary>
public sealed record ClusterRobotInfo(
    RobotStateCode State,
    byte CurrentFace,
    byte AngleNorm,
    byte ExtendNorm,
    bool HasWafer,
    bool ErrorActive,
    double? Pressure,       // TM chamber pressure, Torr; null if no sensor
    bool TurboOn,
    bool ScrollOn);
