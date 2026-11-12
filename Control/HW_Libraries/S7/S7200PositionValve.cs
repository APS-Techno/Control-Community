using APS.SystemControl.BaseHWClasses;

namespace APS.Hardware.S7;

/// <summary>
/// A valve whose displayed open/closed state comes from a PLC input
/// position-sensor bit (EB area), while its command goes to the output marker
/// (MB area). Command is inherited from <see cref="S7200Valve"/>; only the read
/// is redirected.
///
/// statusAddress / statusBit = the EB position bit; statusInvert = 1 when the
/// sensor is active-low.
/// </summary>
public class S7200PositionValve(
    IPLCController PLC, string id, int address, int channel, string description,
    int statusAddress, int statusBit, int statusInvert)
    : S7200Valve(PLC, id, address, channel, description)
{
    private readonly bool statusInverted = statusInvert != 0;

    protected override bool ReadHW()
    {
        bool raw = PLC.ReadBool((int)Mode.EB, statusAddress, statusBit);
        return statusInverted ? !raw : raw;
    }

    public override bool TryGetBulkReadDescriptor(out (int mode, int address, int byteIndex) descriptor)
    {
        // The bulk path cannot apply inversion; use the per-valve read.
        descriptor = default;
        return false;
    }
}
