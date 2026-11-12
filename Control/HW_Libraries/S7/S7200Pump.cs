using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.S7
{
    public class S7200Pump(IPLCController PLC, string id, int address, int channel, string description) : PlcPump(PLC, ConvertToParams(id, address, description))
    {
        private readonly BaseDeviceParams _params = ConvertToParams(id, address, description);

        private static BaseDeviceParams ConvertToParams(string id, int address, string description)
        {
            return new BaseDeviceParams(id, address, description);
        }

        protected override bool ReadHW()
        {
            return PLC.ReadBool((int)Mode.MB, _params.Address, channel);
        }

        protected override void WriteHW(bool newstate)
        {
            PLC.WriteBool((int)Mode.MB, _params.Address, channel, newstate);
        }


        protected override bool[] ReadStatus()
        {
            bool[] result = new bool[1];
            result[0] = ReadHW(); 
            return result;
        }
    }
}