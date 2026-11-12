using System;
using APS.SystemControl.BaseHWClasses;

namespace APS.SystemControl.Core.Cluster;

/// <summary>
/// Wraps a live hardware read so a transient PLC comms failure
/// (<see cref="PlcCommException"/> — e.g. the PLC/Emulator is unreachable)
/// degrades to "no reading" instead of throwing. This lets the cluster
/// snapshot still build — and the panel render its chamber structure — while
/// hardware is offline; the reading repopulates once the device is back.
/// Only <see cref="PlcCommException"/> is swallowed; any other exception is a
/// real bug and propagates.
/// </summary>
internal static class HwRead
{
    public static double? Safe(Func<double?> read)
    {
        try { return read(); }
        catch (PlcCommException) { return null; }
    }

    public static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (PlcCommException) { return fallback; }
    }
}
