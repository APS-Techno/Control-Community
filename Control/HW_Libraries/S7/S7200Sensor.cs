using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.S7
{
    public class S7200Sensor(IPLCController PLC, string id, int address, string description) : PLCSensor(PLC, ConvertToParams(id, address, description))
    {
        private readonly BaseDeviceParams _params = ConvertToParams(id, address, description);

        private static BaseDeviceParams ConvertToParams(string id, int address, string description)
        {
            return new BaseDeviceParams(id, address, description);
        }


        protected override float ReadHW()
        {
            return PLC.ReadFloat((int)Mode.DB, _params.Address);
        }
    }
}