using APS.ProcessPanel.Contracts;

namespace APS.SystemControl;

/// <summary>
/// Turns a device readback (<c>ParamMsg.Args</c>) into process-panel reading
/// samples. Pure and shape-driven so the channel handlers in FrmMain stay
/// thin and the mapping is unit-tested without a form.
///
/// Every supply publishes voltage, current and power under
/// <c>&lt;device&gt;_V</c>, <c>_I</c>, <c>_W</c>; the panel's spec list decides
/// which of them it draws (DC rows chart V + I, RF rows chart W). Ids the
/// collector has not registered are dropped there, so publishing all three
/// costs nothing.
/// </summary>
internal static class PsuTelemetryMapper
{
    public readonly record struct Sample(string Id, double? Value, ReadingState State);

    /// <summary>
    /// Map a power-supply readback.
    /// <list type="bullet">
    /// <item>Single-channel (<c>BasePowerSupply.GetReadback</c>):
    /// <c>[Voltage, Current, Power, IsOutputOn, ..]</c> → <c>_V</c>, <c>_I</c>, <c>_W</c>.
    /// The classifier runs on the watts only; V and I inherit Ok. Output off
    /// marks all three Off.</item>
    /// <item>Multi-channel analyser:
    /// <c>[V1, I1, V2, I2, V3, I3, V4, I4, Out1..Out4, ..]</c>
    /// → <c>_V1</c>, <c>_I1</c>, <c>_V2</c>, <c>_I2</c>, always Ok — the analyser
    /// meters the line whether or not its output is on. Channels 3 and 4 are
    /// not shown on the panel.</item>
    /// </list>
    /// Any other shape yields nothing.
    /// </summary>
    public static List<Sample> MapPowerSupply(string device, object[] args,
        Func<string, double, ReadingState> classify)
    {
        var result = new List<Sample>(4);

        if (args is [float v, float i, float w, bool on, ..])
        {
            string wId = device + "_W";
            var state = on ? classify(wId, w) : ReadingState.Off;
            var vi    = on ? ReadingState.Ok  : ReadingState.Off;
            result.Add(new(device + "_V", v, vi));
            result.Add(new(device + "_I", i, vi));
            result.Add(new(wId, w, state));
            return result;
        }

        // A bias analyser doubles as a meter: a channel with its output off still
        // measures the bias line (voltage, leakage/plasma current), so its
        // readings stay live regardless of the output flag — Off is reserved
        // for the device itself disconnecting (MarkOfflineByDevice).
        if (args.Length >= 12 && args[8] is bool && args[9] is bool
            && args[0] is float && args[1] is float && args[2] is float && args[3] is float)
        {
            for (int ch = 0; ch < 2; ch++)
            {
                result.Add(new($"{device}_V{ch + 1}", (float)args[ch * 2],     ReadingState.Ok));
                result.Add(new($"{device}_I{ch + 1}", (float)args[ch * 2 + 1], ReadingState.Ok));
            }
        }

        return result;
    }

    /// <summary>
    /// True when <paramref name="args"/> is a single-channel readback
    /// <c>[Voltage, Current, Power, IsOutputOn, ..]</c>; <paramref name="watts"/> is its
    /// Power element. A multi-channel readback has a float at index 3, so it is rejected.
    /// </summary>
    public static bool TryGetSingleChannelWatts(object[] args, out float watts)
    {
        if (args is [float, float, float w, bool, ..])
        {
            watts = w;
            return true;
        }
        watts = 0f;
        return false;
    }

    /// <summary>
    /// Map a serial throttle controller readback:
    /// <c>["Data", pressure mTorr, position %, ..]</c> → <c>&lt;device&gt;_P</c>,
    /// <c>&lt;device&gt;_Pos</c>. The PLC throttle shape <c>[position, setpoint]</c>
    /// has no gauge and yields nothing.
    /// </summary>
    public static List<Sample> MapThrottleController(string device, object[] args)
    {
        var result = new List<Sample>(2);
        if (args is ["Data", float pressure, float position, ..])
        {
            result.Add(new(device + "_P",   pressure, ReadingState.Ok));
            result.Add(new(device + "_Pos", position, ReadingState.Ok));
        }
        return result;
    }
}
