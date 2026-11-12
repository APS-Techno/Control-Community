using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.S7
{
    /// <remarks>
    /// There is no dedicated stage-stop command, so <see cref="Stop"/> re-commands
    /// the stage to its current feedback position. An optional dedicated stop bit
    /// (<c>haltByte</c>/<c>haltBit</c>, read via <c>BaseStage.ApplyConfig</c>) is
    /// pulsed only when configured.
    /// </remarks>
    public class S7200Stage(IPLCController PLC, string id, int address, int addressDB, int index, string description) : PLCStage(PLC, ConvertToParams(id, address, description))
    {
        private readonly BaseDeviceParams _params = ConvertToParams(id, address, description);

        private static BaseDeviceParams ConvertToParams(string id, int address, string description)
        {
            return new BaseDeviceParams(id, address, description);
        }

        protected override float ReadHW()
        {
            return PLC.ReadFloat((int)Mode.DB, address + 4);
        }

        protected override void WriteHW(float position)
        {
            PLC.WriteFloat((int)Mode.DB, address, position);
            PlcPulse.Pulse(PLC, (int)Mode.DB, addressDB, index);
        }

        public override void Stop()
        {
            float current = PLC.ReadFloat((int)Mode.DB, address + 4);
            WriteHW(current);

            if (StopBit >= 0)
            {
                PlcPulse.Pulse(PLC, (int)Mode.DB, StopByte, StopBit);
            }
        }
    }
}
