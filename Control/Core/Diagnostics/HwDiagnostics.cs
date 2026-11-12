using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.Core.Diagnostics;

public record DeviceProbeResult2(
    string DeviceId, string DeviceType, DeviceProbeResult Result, string? ViaPlc);

public record SelfTestReport(bool Ran, string? BlockedReason, IReadOnlyList<DeviceProbeResult2> Results);

/// <summary>
/// Orchestrates on-demand hardware diagnostics. Opens a window once and pings
/// each PLC and each directly-connected (non-PLC-hosted) serial/TCP device.
/// PLC-hosted controls (PlcValve, PLCMFC, PlcPump, PLCStage, PlcSensor etc.)
/// are filtered out — their PLC row covers them. The self-test is read-only and
/// gated to idle (no deposition) so a probe can never perturb a live run.
/// </summary>
public class HwDiagnostics
{
    public SelfTestReport RunSelfTest(
        IEnumerable<IBaseComponent> devices,
        IReadOnlyDictionary<string, IPLCController> plcs,
        bool isDepositionActive)
    {
        if (isDepositionActive)
            return new SelfTestReport(false,
                "Self-test blocked: a deposition is active (recipe running or PSU output on). " +
                "Stop the process and retry.", []);

        var plcResults = new Dictionary<string, DeviceProbeResult>();
        foreach (var (id, plc) in plcs)
            plcResults[id] = plc is ISelfTestable st
                ? SafeProbe(st)
                : new DeviceProbeResult(HealthLevel.MonitoredOnly, false, Error: "no probe");

        var results = new List<DeviceProbeResult2>();

        foreach (var (id, _) in plcs)
            results.Add(new DeviceProbeResult2(id, "PLC", plcResults[id], ViaPlc: null));

        foreach (var dev in devices)
        {
            // Skip PLC-hosted controls — they have no own link; the PLC row covers them.
            if (dev is IBasePLCComponent)
                continue;

            string id = dev.Params.ID;
            string type = dev.Params.Type.ToString();

            DeviceProbeResult result = dev is ISelfTestable selfTest
                ? SafeProbe(selfTest)
                : new DeviceProbeResult(HealthLevel.MonitoredOnly, false, Error: "no active probe");
            results.Add(new DeviceProbeResult2(id, type, result, ViaPlc: null));
        }

        return new SelfTestReport(true, null, results);
    }

    // One dead device must never abort the sweep.
    private static DeviceProbeResult SafeProbe(ISelfTestable d)
    {
        try { return d.Probe(); }
        catch (Exception ex)
        {
            return DeviceProbeResult.Bad(HealthLevel.Unresponsive, ex.Message);
        }
    }
}
