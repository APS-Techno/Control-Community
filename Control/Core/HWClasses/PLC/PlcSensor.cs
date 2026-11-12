using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

public class PLCSensor(IPLCController plc, IBaseDeviceParams Params) : BaseSensor(Params)
{

    public IPLCController PLC { get; private set; } = plc;
}

public class PLCSensorFactory : IPLCDeviceFactory
{
    public IBaseComponent CreateDevice(IPLCController PLC, IBaseDeviceParams Params) => new PLCSensor(PLC, Params);
}