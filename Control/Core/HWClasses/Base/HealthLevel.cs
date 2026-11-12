namespace APS.SystemControl.Core.HWClasses.Base;

/// <summary>
/// Real per-device health, richer than the transport-only <c>Connected</c> flag.
/// </summary>
public enum HealthLevel
{
    /// <summary>Never polled / no data yet.</summary>
    Unknown,
    /// <summary>A valid, parseable reply was seen within the freshness window.</summary>
    Alive,
    /// <summary>Was Alive; no valid reply for a while but still being polled.</summary>
    Stale,
    /// <summary>Repeated silence/timeouts — the device is not replying.</summary>
    Unresponsive,
    /// <summary>Bytes arrive but do not parse (bad CRC/terminator/fields) — wrong protocol.</summary>
    WrongProtocol,
    /// <summary>No active probe available; only passive last-good-read is known.</summary>
    MonitoredOnly,
}

/// <summary>Result of a single read-only liveness probe.</summary>
/// <param name="Ok"><c>true</c> only when <see cref="Level"/> is <see cref="HealthLevel.Alive"/>.</param>
/// <param name="Identity">Model/serial/firmware string where the protocol offers one; else null.</param>
/// <param name="Detail">Human-readable extra (e.g. a measured value); optional.</param>
/// <param name="LatencyMs">Round-trip time of the probe, milliseconds.</param>
/// <param name="Error">Failure description when not Ok; else null.</param>
public record DeviceProbeResult(
    HealthLevel Level,
    bool Ok,
    string? Identity = null,
    string? Detail = null,
    double LatencyMs = 0,
    string? Error = null)
{
    public static DeviceProbeResult Good(double latencyMs, string? identity = null, string? detail = null)
        => new(HealthLevel.Alive, true, identity, detail, latencyMs);

    public static DeviceProbeResult Bad(HealthLevel level, string error, double latencyMs = 0)
        => new(level, false, Error: error, LatencyMs: latencyMs);
}

/// <summary>
/// Optional capability: a device that can prove it is alive with a read-only,
/// state-preserving protocol exchange. Implementations MUST take the device's
/// own I/O lock and MUST NOT change setpoints/output/motion.
/// </summary>
public interface ISelfTestable
{
    DeviceProbeResult Probe();
}

