// DeviceType lives in APS.Controls.Core; a global using alias in GlobalUsings.cs
// resolves the unqualified symbol.

namespace APS.SystemControl.Core;

public enum MsgType { Error, Value, Status, Command, Log, Progress, Alarm, Interlock, OverrideFlash};
public enum HWStatus { Connected, Error, Disconnected };

public class ThreadMsg(MsgType type, DeviceType target, string device, int channel = 0) : IDisposable
{
    public MsgType Type { get; set; } = type;
    public DeviceType Target { get; set; } = target;
    public string? Device { get; set; } = device;
    public int Channel { get; set; } = channel;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

public class ValMsg<TValue>(MsgType type, DeviceType target, string device, TValue value, int channel = 0) : ThreadMsg(type, target, device, channel)
{
    public TValue Value { get; set; } = value;
}


public class BoolMsg(MsgType type, DeviceType target, string device, bool[] value) : ThreadMsg(type, target, device, 0)
{
    public bool[] Value = value;
}

public class ParamMsg(MsgType type, DeviceType target, string operation, object[] args) : ThreadMsg(type, target, operation, 0)
{
    public object[] Args = args;
    public string Operation = operation;
}

