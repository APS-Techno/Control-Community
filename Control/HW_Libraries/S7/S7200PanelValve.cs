using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.S7;

/// <summary>
/// Valve opened or closed by a momentary pulse on a paired command flag in DB1,
/// with its open/closed state read from a position sensor in the EB (input) area.
/// A direct write of the output marker would be reverted by firmware.
///
/// Parameters:
///   address / channel     = EB position-sensor byte / bit (read back for status)
///   openAddress / openBit   = DB1 open-command flag (pulsed)
///   closeAddress / closeBit = DB1 close-command flag (pulsed)
///   statusInvert            = 1 when the sensor is the closed-limit (open = NOT bit)
/// </summary>
public class S7200PanelValve(
    IPLCController PLC, string id, int address, int channel, string description,
    int openAddress, int openBit, int closeAddress, int closeBit, int statusInvert)
    : PlcValve(PLC, ParamsConverter.Params(id, address, description))
{
    private const int PulseMs = 250;

    /// <summary>
    /// Upper bound on the travel time of the slowest valve. The pulse returns
    /// before the position sensor trips, so the valve op waits up to this long
    /// for the read-back and stops waiting as soon as the sensor agrees.
    /// </summary>
    internal const int SettleMs = 20_000;

    public override int SettleTimeoutMs => SettleMs;
    private readonly int statusBit = channel;
    private readonly bool statusInverted = statusInvert != 0;

    protected override bool ReadHW()
    {
        bool raw = PLC.ReadBool((int)Mode.EB, Params.Address, statusBit);
        return statusInverted ? !raw : raw;
    }

    protected override void WriteHW(bool newstate)
    {
        int addr = newstate ? openAddress : closeAddress;
        int bit = newstate ? openBit : closeBit;
        PlcPulse.Pulse(PLC, (int)Mode.DB, addr, bit, PulseMs);
    }

    public override bool TryGetBulkReadDescriptor(out (int mode, int address, int byteIndex) descriptor)
    {
        // Status is an EB sensor and may be inverted; the bulk path caches the raw
        // bit without inversion, so opt out and use the per-valve read.
        descriptor = default;
        return false;
    }
}
