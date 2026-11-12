using System.Threading.Channels;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// The decoupled-bus-polling coordinator reads device LastX caches. Those caches
/// start cold (sensor LastValue 0, pump LastStatus empty) until a bus poller runs.
/// If the coordinator's first cycle wins the race it reads the cold defaults —
/// a pm of 0 Torr fires a spurious startup->idle transition (then a false
/// "pressure too high" alarm), and an empty pump status crashes the turbo widget.
/// SeedDeviceCaches() reads every device once before the coordinator starts so the
/// first read sees real values.
/// </summary>
public class SeedDeviceCachesTests
{
    private sealed class TestSensor : BaseSensor
    {
        public float Hw;
        public TestSensor() : base(new BaseDeviceParams("pm", 0, "")) { Connected = true; }
        protected override float ReadHW() => Hw;
    }

    private sealed class TestPump : BasePump
    {
        public bool[] Hw = [];
        public TestPump() : base(new BaseDeviceParams("TP", 0, "")) { Connected = true; }
        protected override bool[] ReadStatus() => Hw;
    }

    [Fact]
    public void SeedDeviceCaches_populates_device_caches_before_first_poll()
    {
        var ch = Channel.CreateUnbounded<ThreadMsg>();
        var core = new APS.SystemControl.Core.Core(ch.Writer);
        var sensor = new TestSensor { Hw = 0.02f };           // 20 mTorr (Torr)
        var pump = new TestPump { Hw = [true, false, false, false] };
        core.hwElements.Item[DeviceType.Sensor].Add("pm", sensor);
        core.hwElements.Item[DeviceType.Pump].Add("TP", pump);

        Assert.Equal(0f, sensor.LastValue);
        Assert.Empty(pump.LastStatus);

        core.SeedDeviceCaches();

        Assert.Equal(0.02f, sensor.LastValue);
        Assert.Equal(4, pump.LastStatus.Length);
    }

    [Fact]
    public void SeedDeviceCaches_skips_disconnected_devices()
    {
        var ch = Channel.CreateUnbounded<ThreadMsg>();
        var core = new APS.SystemControl.Core.Core(ch.Writer);
        var sensor = new TestSensor { Hw = 5f, Connected = false };
        core.hwElements.Item[DeviceType.Sensor].Add("pm", sensor);

        core.SeedDeviceCaches();

        Assert.Equal(0f, sensor.LastValue);
    }
}
