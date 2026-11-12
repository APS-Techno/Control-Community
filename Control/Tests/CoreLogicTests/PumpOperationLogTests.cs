using System.Threading.Channels;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// The Operation log records what the operator COMMANDED, not the measured state.
/// A turbo needs ~30 s to spin up, so sampling state right after the command would
/// log "on" as off, and "off" as on while it coasts down. Actual running state is
/// reported by the widget, by S7200TurboPump.LogStatusChange's raw DB17 trace, and
/// by the TP alarm rules.
/// </summary>
public class PumpOperationLogTests
{
    /// <summary>
    /// A pump decoded the way <c>S7200TurboPump</c> decodes: status[0] is the ACCEL
    /// transient and must NOT be read as "running"; running is status[1] || status[2].
    /// </summary>
    private sealed class TurboLikePump : BasePump
    {
        public bool[] Hw = [false, false, false, false];
        public TurboLikePump() : base(new BaseDeviceParams("TP", 17, "")) { Connected = true; }
        protected override bool[] ReadStatus() => Hw;
        protected override bool IsRunningFrom(bool[] status)
            => status.Length >= 3 && (status[1] || status[2]);
    }

    private static APS.SystemControl.Core.Core NewCore()
        => new(Channel.CreateUnbounded<ThreadMsg>().Writer);

    [Fact]
    public async Task Commanding_a_cold_turbo_on_logs_on_not_off()
    {
        var core = NewCore();
        var pump = new TurboLikePump();          // all status bits clear: not spinning

        var result = await core.OpPump([pump, true], CancellationToken.None);

        Assert.Equal(
            "Pump TP on",
            APS.SystemControl.Core.CoreThread.ResToStr(DeviceType.Pump, "TP", result!));
    }

    /// <summary>
    /// A status[0] decoder also logs "off" here, so this case only guards the
    /// direction: a change that starts reporting the measured state must not
    /// silently invert it. The ON case above is the one that discriminates.
    /// </summary>
    [Fact]
    public async Task Commanding_a_spinning_turbo_off_logs_off()
    {
        var core = NewCore();
        var pump = new TurboLikePump { Hw = [false, true, true, false] };   // at speed

        var result = await core.OpPump([pump, false], CancellationToken.None);

        Assert.Equal(
            "Pump TP off",
            APS.SystemControl.Core.CoreThread.ResToStr(DeviceType.Pump, "TP", result!));
    }

    /// <summary>
    /// Core.LoadWorker registers "Pump" as returning <c>typeof(bool)</c>; ResToStr
    /// renders a bool[] from its element 0, which for a turbo is the ACCEL transient.
    /// </summary>
    [Fact]
    public async Task OpPump_returns_a_bool_matching_its_registered_return_type()
    {
        var core = NewCore();
        var pump = new TurboLikePump();

        var result = await core.OpPump([pump, true], CancellationToken.None);

        Assert.IsType<bool>(result);
        Assert.True((bool)result!);
    }
}
