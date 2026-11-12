using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

public class BaseSensor : IBaseComponent
{
    protected bool _connected;

    public BaseSensor(IBaseDeviceParams Params)
    {
        this.Params = Params;
        this.Params.Type = DeviceType.Sensor;
    }

    public IBaseDeviceParams Params { get; private set; }

    /// <summary>
    /// Chamber this sensor reads. Set during hardware load from the
    /// <c>chamber="..."</c> attribute; defaults to <see cref="CoreConstants.DefaultChamberId"/>
    /// when the attribute is absent.
    /// </summary>
    public string Chamber { get; set; } = CoreConstants.DefaultChamberId;

    public bool Connected { get => _connected; set { _connected = value; } }

    /// <summary>
    /// Opens the sensor's connection. PLC-hosted sensors inherit the
    /// no-op default since they read through the PLC's shared socket
    /// (their <see cref="Connected"/> is set from the PLC at load time);
    /// serial sensors (e.g. Modbus-RTU) must override to open
    /// their port. Called once during hardware load.
    /// </summary>
    public virtual bool Connect() => Connected;

    /// <summary>
    /// Last value returned by <see cref="Value"/>, refreshed every time the
    /// background poll reads this sensor. UI widgets (cluster panel, transfer-
    /// module widget) must read THIS instead of calling <see cref="Value"/> —
    /// reading it is a pure in-memory access with no PLC I/O, so it cannot block
    /// the UI thread, and it keeps the PLC socket single-reader (poll thread only).
    /// </summary>
    public float LastValue { get; private set; }

    public void Poll() { if (_connected) Value(); }

    public float Value()
    {
        LastValue = ReadHW();
        return LastValue;
    }

    protected virtual float ReadHW() => 0;

}

public class SensorFactory : IDeviceFactory
{
    public IBaseComponent CreateDevice(IBaseDeviceParams Params) => new BaseSensor(Params);
}
