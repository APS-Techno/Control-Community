using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;

namespace APS.SystemControl.Core.HWClasses.Base;

public interface IBaseDeviceParams
{
    public string ID { get; }
    public DeviceType Type { get; set; }
    public int Address { get; }
    public string Description { get; }
}

public interface IBaseComponent
{
    public IBaseDeviceParams Params { get; }
    public bool Connected { get; set; }

    /// <summary>
    /// Refresh this device's in-memory cache (LastX) from hardware. Called only
    /// by the device's bus poller. Default no-op (e.g. drives, which poll on
    /// their own loop). No-op when <see cref="Connected"/> is false.
    /// </summary>
    void Poll() { }
}


public interface IBasePLCComponent : IBaseComponent
{
    public IPLCController PLC { get; }
}

public interface IDeviceFactory
{
    IBaseComponent CreateDevice(IBaseDeviceParams deviceParams);
}


public interface IPLCDeviceFactory
{
    IBaseComponent CreateDevice(IPLCController Pplc, IBaseDeviceParams Pparams);
}


public class BaseDeviceParams(string id, int address, string description) : IBaseDeviceParams
{
    public string ID { get; } = id;
    public int Address { get; } = address;
    public string Description { get; } = description;
    public DeviceType Type { get; set; } = DeviceType.Undefined;
}

public static class ParamsConverter
{
    public static BaseDeviceParams Params(string id, int address, string description)
    {
        return new BaseDeviceParams(id, address, description);
    }
}
