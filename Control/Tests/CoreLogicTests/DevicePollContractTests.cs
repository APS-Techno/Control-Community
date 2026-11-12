using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class DevicePollContractTests
{
    private sealed class FakeSensor : BaseSensor
    {
        public float Hw;
        public FakeSensor() : base(new BaseDeviceParams("s", 0, "")) { Connected = true; }
        protected override float ReadHW() => Hw;
    }

    private sealed class FakeController : BaseController
    {
        public object[] Hw = [];
        public FakeController() : base(new BaseDeviceParams("c", 0, "")) { Connected = true; }
        public override object[] Measure() => Hw;
    }

    [Fact]
    public void Poll_refreshes_sensor_LastValue()
    {
        var s = new FakeSensor { Hw = 4.2f };
        s.Poll();
        Assert.Equal(4.2f, s.LastValue);
    }

    [Fact]
    public void Poll_is_noop_when_disconnected()
    {
        var s = new FakeSensor { Hw = 9f, Connected = false };
        s.Poll();
        Assert.Equal(0f, s.LastValue);   // never read
    }

    [Fact]
    public void Controller_Poll_caches_last_measurement()
    {
        var c = new FakeController { Hw = [1.0, 2.0] };
        c.Poll();
        Assert.Equal(new object[] { 1.0, 2.0 }, c.LastMeasurement);
    }

    [Fact]
    public void Controller_LastMeasurement_starts_empty()
    {
        var c = new FakeController();
        Assert.Empty(c.LastMeasurement);
    }
}
