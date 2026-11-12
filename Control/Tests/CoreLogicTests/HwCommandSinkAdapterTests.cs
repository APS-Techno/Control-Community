using APS.Controls.Core;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Menus.Services;
using Xunit;

namespace CoreLogicTests;

/// <summary>Control-mode gate on the XML-menu → hardware adapter (spec 2026-09-06 §4.3).</summary>
public class HwCommandSinkAdapterTests
{
    private static string? RemoteGate(CommandClass cls) =>
        cls == CommandClass.Safety ? null : ControlModeService.OperatorDenial;

    private static ThreadMsg? Sent(ChannelPool pool) => pool.HWReader!.TryRead(out var m) ? m : null;

    [Fact]
    public void Without_a_gate_every_command_is_forwarded_as_a_ParamMsg()
    {
        var pool = new ChannelPool();
        new HwCommandSinkAdapter(pool).SendCommand(DeviceType.Drive, "Holder", "MoveToFlange", 1);
        var msg = Assert.IsType<ParamMsg>(Sent(pool));
        Assert.Equal((MsgType.Command, DeviceType.Drive, "Holder"), (msg.Type, msg.Target, msg.Device));
        Assert.Equal(new object[] { "MoveToFlange", 1 }, msg.Args);
    }

    [Fact]
    public void Mutating_command_is_refused_in_Remote_logged_and_not_sent()
    {
        var pool = new ChannelPool();
        var log = new List<string>();
        new HwCommandSinkAdapter(pool, RemoteGate, log.Add).SendCommand(DeviceType.Drive, "Holder", "MoveToFlange", 1);
        Assert.Null(Sent(pool));
        var line = Assert.Single(log);
        Assert.Contains("Drive Holder MoveToFlange 1", line);
        Assert.Contains("switch to Local", line);
    }

    [Fact]
    public void Stop_is_a_safety_action_and_passes_in_Remote()
    {
        var pool = new ChannelPool();
        var log = new List<string>();
        new HwCommandSinkAdapter(pool, RemoteGate, log.Add).SendCommand(DeviceType.Drive, "Holder", "Stop");
        var msg = Assert.IsType<ParamMsg>(Sent(pool));
        Assert.Equal(new object[] { "Stop" }, msg.Args);
        Assert.Empty(log);
    }
}
