using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.S7
{
    /// <summary>
    /// Rotation stage: writes a float speed setpoint to a DB address and flips a
    /// memory-area (M) enable bit. speed &gt; 1 enables rotation at the requested
    /// rpm; speed &lt;= 1 disables it without changing the stored setpoint. Read
    /// returns the live speed from feedbackAddr.
    /// </summary>
    public class S7200RotationStage(
        IPLCController PLC,
        string id,
        int address,
        int feedbackAddr,
        int enableByte,
        int enableBit,
        string description)
        : PLCStage(PLC, new BaseDeviceParams(id, address, description))
    {
        protected override float ReadHW()
            => PLC.ReadFloat((int)Mode.DB, feedbackAddr);

        protected override void WriteHW(float speed)
        {
            if (speed > 1f)
            {
                PLC.WriteFloat((int)Mode.DB, address, speed);
                PLC.WriteBool((int)Mode.MB, enableByte, enableBit, true);
            }
            else
            {
                PLC.WriteBool((int)Mode.MB, enableByte, enableBit, false);
            }
        }
    }
}
