using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.S7;

/// <summary>
/// Valve whose state is read from a PLC input bit (EB area), inverted:
/// bit 0 = open, bit 1 = closed.
/// </summary>
public class S7200InputValve(IPLCController PLC, string id, int address, int channel, string description) : PlcValve(PLC, ParamsConverter.Params(id, address, description))
{
    private readonly int channel = channel;

    protected override bool ReadHW()
    {
        return !PLC.ReadBool((int)Mode.EB, Params.Address, channel);
    }

    protected override void WriteHW(bool newstate)
    {
        // Write inverted signal to EB (matches ReadHW inversion: EB 0 = open)
        PLC.WriteBool((int)Mode.EB, Params.Address, channel, !newstate);
    }
}
