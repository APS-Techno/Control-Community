using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

public class PlcPump(IPLCController plc, IBaseDeviceParams Params) : BasePump(Params)
{

    public IPLCController PLC { get; private set; } = plc;

    public override void Power(bool state)
    {
        // Idempotent on: re-commanding an already-running pump is a no-op.
        // Mirrors BasePump.Power — PlcPump fully overrides Power (it adds the
        // PLC.Connected check), so the guard must be repeated here or it never
        // runs for real pumps (all real pumps derive from PlcPump). 'off' is
        // never suppressed.
        if (state && IsRunning)
            return;
        if (PLC.Connected && _connected)
            WriteHW(state);
    }
}

public class PLCPumpFactory : IPLCDeviceFactory
{
    public IBaseComponent CreateDevice(IPLCController PLC, IBaseDeviceParams Params) => new PlcPump(PLC, Params);
}
