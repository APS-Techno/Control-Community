using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

public class PLCMFC : BaseMFC
{
    public PLCMFC(IPLCController plc, IBaseDeviceParams Params) : base(Params) => PLC = plc;

    public IPLCController PLC { get; private set; }

    /// <summary>
    /// Raised when a verified write to the PLC fails after the maximum number
    /// of retry attempts. Subscribed by Core.LoadHardware to publish a Log
    /// message on the LOG channel and a Status message on the GUI channel —
    /// so operator and downstream systems learn that the recipe-level setpoint
    /// did not actually reach the PLC.
    /// </summary>
    /// <remarks>
    /// THREADING: Raised synchronously on the PLC polling thread. Handlers
    /// must be non-blocking — typically a single non-blocking write to a
    /// Channel or a Trace call. Heavier work (file I/O, network, locks)
    /// will stall the 200 ms polling cycle.
    /// </remarks>
    public event Action<float>? WriteFailed;

    protected void RaiseWriteFailed(float value)
    {
        LastWriteVerified = false;
        WriteFailed?.Invoke(value);
    }

    public override void SetFlow(float value)
    {
        if (!PLC.Connected) return;
        base.SetFlow(value);
    }
}

public class MPLCFCFactory : IPLCDeviceFactory
{
    public IBaseComponent CreateDevice(IPLCController PLC, IBaseDeviceParams Params) => new PLCMFC(PLC, Params);
}

