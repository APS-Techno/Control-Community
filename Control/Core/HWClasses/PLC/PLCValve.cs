using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

public class PlcValve : BaseValve
{
    private readonly IPLCController plc;

    public PlcValve(IPLCController plc, IBaseDeviceParams Params) : base(Params) => this.plc = plc;

    public IPLCController PLC => plc;

    /// <summary>
    /// Raised when a verified bool write to the PLC fails after the maximum
    /// number of retry attempts. Subscribed by Core.LoadHardware to publish a
    /// Log entry so the operator learns the valve never reached the requested
    /// state. The bool argument is the target state that failed (true=Open).
    /// </summary>
    /// <remarks>
    /// THREADING: Raised synchronously on the PLC polling thread. Handlers
    /// must be non-blocking — typically a single non-blocking write to a
    /// Channel or a Trace call. Heavier work (file I/O, network, locks)
    /// will stall the 200 ms polling cycle.
    /// </remarks>
    public event Action<bool>? WriteFailed;

    protected void RaiseWriteFailed(bool target)
    {
        LastWriteVerified = false;
        WriteFailed?.Invoke(target);
    }

    /// <summary>
    /// Exposes the (mode, address, bit) tuple needed for a bulk PLC read.
    /// Returns false on the base — only concrete PLC valve drivers that
    /// participate in bulk reads override this. Core uses the descriptor
    /// to gather requests across valves and issue a single round-trip per
    /// PLC instead of one per valve.
    /// </summary>
    public virtual bool TryGetBulkReadDescriptor(out (int mode, int address, int byteIndex) descriptor)
    {
        descriptor = default;
        return false;
    }
}


public class PLCValveFactory : IPLCDeviceFactory
{
    public IBaseComponent CreateDevice(IPLCController PLC, IBaseDeviceParams Params) => new PlcValve(PLC, Params);
}
