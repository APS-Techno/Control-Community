namespace APS.SystemControl.Plugin;

/// <summary>
/// Routes a command from a menu contributor to the HW thread. Concrete
/// implementation (in main app) wraps ChannelPool.WriteHW with a ParamMsg.
/// </summary>
public interface IHwCommandSink
{
    /// <summary>
    /// args[0] is conventionally the command name (e.g. "SetOffset"); subsequent
    /// args are command-specific values, matching the panel→HW protocol.
    /// </summary>
    void SendCommand(DeviceType type, string deviceId, params object[] args);
}
