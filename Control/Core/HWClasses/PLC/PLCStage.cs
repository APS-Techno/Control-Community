using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

public class PLCStage : BaseStage
{
    public PLCStage(IPLCController plc, IBaseDeviceParams Params) : base(Params) => PLC = plc;

    public IPLCController PLC { get; private set; }
}

public class PLCStageFactory : IPLCDeviceFactory
{
    public IBaseComponent CreateDevice(IPLCController PLC, IBaseDeviceParams Params) => new PLCStage(PLC, Params);
}