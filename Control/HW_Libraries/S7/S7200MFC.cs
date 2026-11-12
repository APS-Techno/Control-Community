using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.S7;

public class S7200MFC(IPLCController PLC, string id, int address, string description) : PLCMFC(PLC, ConvertToParams(id, address, description))
{

    private readonly BaseDeviceParams _params = ConvertToParams(id, address, description);

    private const int VerifyMaxAttempts = 5;
    private const float VerifyToleranceSccm = 0.5f;

    private static BaseDeviceParams ConvertToParams(string id, int address, string description)
    {
        return new BaseDeviceParams(id, address, description);
    }


    protected override float ReadHW()
    {
        return PLC.ReadFloat((int)Mode.DB, _params.Address + 4);
    }

    /// <summary>The setpoint lives at offset 0 of the MFC DB block; actual flow is at +4.</summary>
    protected override float ReadSetpointHW()
    {
        return PLC.ReadFloat((int)Mode.DB, _params.Address);
    }

    protected override void WriteHW(float newvalue)
    {
        bool ok = PlcVerifiedWrites.WriteFloatVerified(
            PLC, (int)Mode.DB, _params.Address,
            value: newvalue, tolerance: VerifyToleranceSccm, maxAttempts: VerifyMaxAttempts);
        if (!ok)
        {
            System.Diagnostics.Trace.TraceError(
                $"S7200MFC[{_params.ID}] write {newvalue:F2} sccm failed verification after {VerifyMaxAttempts} attempts.");
            RaiseWriteFailed(newvalue);
        }
    }
}
