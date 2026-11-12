using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

/// <summary>
/// PLC-backed robot base. Holds the <see cref="IPLCController"/> reference
/// and the DB address/offset layout for the concrete device to use.
/// Subclasses (e.g. <c>S7200Robot</c>) implement <see cref="BaseRobot.Poll"/>,
/// <see cref="BaseRobot.Transfer"/>, <see cref="BaseRobot.Home"/>, and
/// <see cref="BaseRobot.Abort"/> using the PLC-specific <c>Mode</c> constant
/// and the byte-read primitives available in their own assembly.
/// </summary>
public abstract class PLCRobot : BaseRobot
{
    protected readonly IPLCController PLC;

    /// <summary>DB base address of the robot memory block.</summary>
    protected readonly int address;

    /// <summary>Byte offset within the block where the command word starts.</summary>
    protected readonly int cmdOffset;

    /// <summary>Byte offset within the block where the status word starts.</summary>
    protected readonly int statusOffset;

    protected PLCRobot(IPLCController plc, IBaseDeviceParams parms,
                       int address, int cmdOffset, int statusOffset)
        : base(parms)
    {
        PLC = plc;
        this.address = address;
        this.cmdOffset = cmdOffset;
        this.statusOffset = statusOffset;
    }
}
