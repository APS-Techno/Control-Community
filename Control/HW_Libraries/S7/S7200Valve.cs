using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.S7;

public class S7200Valve(IPLCController PLC, string id, int address, int channel, string description) : PlcValve(PLC, ParamsConverter.Params(id, address, description))
{
    private readonly int channel = channel;
    private const int VerifyMaxAttempts = 5;

    protected override bool ReadHW()
    {
        return PLC.ReadBool((int)Mode.MB, Params.Address, channel);
    }

    protected override void WriteHW(bool newstate)
    {
        bool ok = PlcVerifiedWrites.WriteBoolVerified(
            PLC, (int)Mode.MB, Params.Address, channel,
            value: newstate, maxAttempts: VerifyMaxAttempts);
        if (!ok)
        {
            System.Diagnostics.Trace.TraceError(
                $"S7200Valve[{Params.ID}] write {(newstate ? "OPEN" : "CLOSE")} failed verification after {VerifyMaxAttempts} attempts.");
            RaiseWriteFailed(newstate);
        }
    }

    public override bool TryGetBulkReadDescriptor(out (int mode, int address, int byteIndex) descriptor)
    {
        descriptor = ((int)Mode.MB, Params.Address, channel);
        return true;
    }
}
