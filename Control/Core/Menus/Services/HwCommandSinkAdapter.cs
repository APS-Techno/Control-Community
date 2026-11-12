using APS.SystemControl.Plugin;

namespace APS.SystemControl.Core.Menus.Services;

/// <summary>Adapts ChannelPool.WriteHW to IHwCommandSink. Same protocol the existing panel→HW
/// callbacks use (ParamMsg with [command, ...args]). Operator origin: refused while the control
/// mode is Remote (spec 2026-09-06 §4.3) except a leading "Stop" command, which is a safety action
/// like the panel and Stream Deck Stop; the plugin contract itself is unchanged.</summary>
internal sealed class HwCommandSinkAdapter : IHwCommandSink
{
    private readonly ChannelPool _pool;
    private readonly Func<CommandClass, string?>? _authorize;
    private readonly Action<string>? _log;

    public HwCommandSinkAdapter(ChannelPool pool, Func<CommandClass, string?>? authorize = null, Action<string>? log = null)
    { _pool = pool; _authorize = authorize; _log = log; }

    public void SendCommand(DeviceType type, string deviceId, params object[] args)
    {
        var cls = args is [string first, ..] && first.Equals("Stop", StringComparison.OrdinalIgnoreCase)
            ? CommandClass.Safety : CommandClass.Mutate;
        string? deny = _authorize?.Invoke(cls);
        if (deny is not null)
        {
            _log?.Invoke($"[ControlMode] refused {type} {deviceId} {string.Join(' ', args)}: {deny}");
            return;
        }
        _pool.WriteHW(new ParamMsg(MsgType.Command, type, deviceId, args));
    }
}
